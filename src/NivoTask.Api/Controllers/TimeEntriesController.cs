using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NivoTask.Api.Data;
using NivoTask.Api.Models;
using Microsoft.AspNetCore.Authorization;
using NivoTask.Shared.Dtos.TimeEntries;

namespace NivoTask.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class TimeEntriesController : ControllerBase
{
    private readonly AppDbContext _db;

    public TimeEntriesController(AppDbContext db) => _db = db;

    [HttpPost("tasks/{taskId}/timer/start")]
    public async Task<ActionResult<TimeEntryResponse>> StartTimer(int taskId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var task = await _db.Tasks
            .Include(t => t.Column).ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.Column.Board.UserId == userId);

        if (task is null) return NotFound();

        // Proactive conflict check -- return 409 with active timer details (D-01)
        // Filter: EndTime == null AND StartTime != null (excludes manual entries)
        var activeEntry = await _db.TimeEntries
            .Include(te => te.Task)
            .Include(te => te.Board)
            .FirstOrDefaultAsync(te => te.UserId == userId && te.EndTime == null && te.StartTime != null);

        if (activeEntry is not null)
        {
            return Conflict(new ActiveTimerResponse
            {
                EntryId = activeEntry.Id,
                TaskId = activeEntry.TaskId,
                TaskTitle = activeEntry.Task?.Title,
                BoardId = activeEntry.BoardId,
                BoardName = activeEntry.Board?.Name ?? string.Empty,
                ElapsedSeconds = Math.Max(0, (int)(DateTime.UtcNow - activeEntry.StartTime!.Value).TotalSeconds),
                StartTime = activeEntry.StartTime!.Value
            });
        }

        var entry = new TimeEntry
        {
            TaskId = taskId,
            BoardId = task.Column.BoardId,
            UserId = userId,
            StartTime = DateTime.UtcNow,
            EndTime = null,
            DurationSeconds = 0
        };

        try
        {
            _db.TimeEntries.Add(entry);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Self-healing: clean ghost entries (manual entries with EndTime=NULL from old bug)
            _db.ChangeTracker.Clear();
            var ghosts = await _db.TimeEntries
                .Where(te => te.UserId == userId && te.EndTime == null && te.StartTime == null)
                .ToListAsync();
            if (ghosts.Count > 0)
            {
                foreach (var g in ghosts) g.EndTime = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                _db.TimeEntries.Add(new TimeEntry
                {
                    TaskId = taskId, BoardId = task.Column.BoardId, UserId = userId,
                    StartTime = DateTime.UtcNow, EndTime = null, DurationSeconds = 0
                });
                try
                {
                    await _db.SaveChangesAsync();
                    return CreatedAtAction(nameof(GetTimeEntries), new { taskId }, ToTimeEntryResponse(entry, task.Title));
                }
                catch { }
            }
            return Conflict(new { message = "Another timer was started concurrently." });
        }

        return CreatedAtAction(nameof(GetTimeEntries), new { taskId }, ToTimeEntryResponse(entry, task.Title));
    }

    [HttpPost("tasks/{taskId}/timer/stop")]
    public async Task<ActionResult<TimeEntryResponse>> StopTimer(int taskId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var task = await _db.Tasks
            .Include(t => t.Column).ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.Column.Board.UserId == userId);

        if (task is null) return NotFound();

        var entry = await _db.TimeEntries
            .FirstOrDefaultAsync(te => te.UserId == userId && te.TaskId == taskId && te.EndTime == null && te.StartTime != null);

        if (entry is null) return NotFound();

        var now = DateTime.UtcNow;
        var duration = (int)(now - entry.StartTime!.Value).TotalSeconds;
        entry.DurationSeconds = Math.Min(duration, 86400);
        entry.EndTime = now;

        await _db.SaveChangesAsync();
        return Ok(ToTimeEntryResponse(entry, task.Title));
    }

    // TODO: Stale-timer detection. If a timer runs for > 24h (orphaned due to
    // browser close / app crash), it never contributes to rollup. WR-03.
    [HttpGet("timer/active")]
    public async Task<IActionResult> GetActiveTimer()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var entry = await _db.TimeEntries
            .Include(te => te.Task)
            .Include(te => te.Board)
            .FirstOrDefaultAsync(te => te.UserId == userId && te.EndTime == null && te.StartTime != null);

        if (entry is null) return NoContent();

        return Ok(new ActiveTimerResponse
        {
            EntryId = entry.Id,
            TaskId = entry.TaskId,
            TaskTitle = entry.Task?.Title,
            BoardId = entry.BoardId,
            BoardName = entry.Board?.Name ?? string.Empty,
            ElapsedSeconds = Math.Max(0, (int)(DateTime.UtcNow - entry.StartTime!.Value).TotalSeconds),
            StartTime = entry.StartTime!.Value
        });
    }

    [HttpGet("time-entries/summary")]
    public async Task<ActionResult<TimeSummaryResponse>> GetSummary()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var todayStart = DateTime.Now.Date;
        var weekStart = todayStart.AddDays(-6);

        var todayStartUtc = todayStart.ToUniversalTime();
        var weekStartUtc = weekStart.ToUniversalTime();

        var entries = await _db.TimeEntries
            .Where(te => te.UserId == userId && te.EndTime != null)
            .Select(te => new { te.DurationSeconds, Stamp = te.EndTime!.Value })
            .Where(x => x.Stamp >= weekStartUtc)
            .ToListAsync();

        var today = entries.Where(e => e.Stamp >= todayStartUtc).ToList();
        var week = entries;

        return Ok(new TimeSummaryResponse
        {
            TodaySeconds = today.Sum(e => e.DurationSeconds),
            WeekSeconds = week.Sum(e => e.DurationSeconds),
            TodayEntryCount = today.Count,
            WeekEntryCount = week.Count
        });
    }

    [HttpGet("tasks/{taskId}/time-entries")]
    public async Task<ActionResult<List<TimeEntryResponse>>> GetTimeEntries(int taskId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var task = await _db.Tasks
            .Include(t => t.Column).ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.Column.Board.UserId == userId);

        if (task is null) return NotFound();

        var entries = await _db.TimeEntries
            .Where(te => te.TaskId == taskId && te.UserId == userId)
            .OrderByDescending(te => te.StartTime ?? DateTime.MinValue)
            .ThenByDescending(te => te.Id)
            .ToListAsync();

        var response = entries.Select(te => ToTimeEntryResponse(te, task.Title)).ToList();

        return Ok(response);
    }

    [HttpPost("tasks/{taskId}/time-entries")]
    public async Task<ActionResult<TimeEntryResponse>> CreateTimeEntry(int taskId, CreateTimeEntryRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var task = await _db.Tasks
            .Include(t => t.Column).ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.Column.Board.UserId == userId);

        if (task is null) return NotFound();

        var now = DateTime.UtcNow;
        var entry = new TimeEntry
        {
            TaskId = taskId,
            BoardId = task.Column.BoardId,
            UserId = userId,
            StartTime = null,
            EndTime = now,
            DurationSeconds = request.DurationMinutes * 60,
            Notes = request.Notes
        };

        _db.TimeEntries.Add(entry);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTimeEntries), new { taskId }, ToTimeEntryResponse(entry, task.Title));
    }

    // ---------- Board-scoped endpoints ----------

    [HttpPost("boards/{boardId}/timer/start")]
    public async Task<ActionResult<TimeEntryResponse>> StartBoardTimer(int boardId, StartBoardTimerRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var board = await _db.Boards.FirstOrDefaultAsync(b => b.Id == boardId && b.UserId == userId);
        if (board is null) return NotFound();

        TaskItem? task = null;
        if (request.TaskId.HasValue)
        {
            task = await _db.Tasks
                .Include(t => t.Column)
                .FirstOrDefaultAsync(t => t.Id == request.TaskId.Value && t.Column.BoardId == boardId);
            if (task is null) return BadRequest("Task does not belong to this board.");
        }

        // Proactive conflict check
        var activeEntry = await _db.TimeEntries
            .Include(te => te.Task)
            .Include(te => te.Board)
            .FirstOrDefaultAsync(te => te.UserId == userId && te.EndTime == null && te.StartTime != null);

        if (activeEntry is not null)
        {
            return Conflict(new ActiveTimerResponse
            {
                EntryId = activeEntry.Id,
                TaskId = activeEntry.TaskId,
                TaskTitle = activeEntry.Task?.Title,
                BoardId = activeEntry.BoardId,
                BoardName = activeEntry.Board?.Name ?? string.Empty,
                ElapsedSeconds = Math.Max(0, (int)(DateTime.UtcNow - activeEntry.StartTime!.Value).TotalSeconds),
                StartTime = activeEntry.StartTime!.Value
            });
        }

        var entry = new TimeEntry
        {
            TaskId = task?.Id,
            BoardId = boardId,
            UserId = userId,
            StartTime = DateTime.UtcNow,
            EndTime = null,
            DurationSeconds = 0,
            Notes = request.Notes
        };

        try
        {
            _db.TimeEntries.Add(entry);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var ghosts = await _db.TimeEntries
                .Where(te => te.UserId == userId && te.EndTime == null && te.StartTime == null)
                .ToListAsync();
            if (ghosts.Count > 0)
            {
                foreach (var g in ghosts) g.EndTime = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            return Conflict(new { message = "Another timer was started concurrently." });
        }

        return CreatedAtAction(nameof(GetBoardTimeEntries), new { boardId }, ToTimeEntryResponse(entry, task?.Title));
    }

    [HttpPost("boards/{boardId}/timer/stop")]
    public async Task<ActionResult<TimeEntryResponse>> StopBoardTimer(int boardId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var board = await _db.Boards.AnyAsync(b => b.Id == boardId && b.UserId == userId);
        if (!board) return NotFound();

        var entry = await _db.TimeEntries
            .Include(te => te.Task)
            .FirstOrDefaultAsync(te => te.UserId == userId && te.BoardId == boardId
                && te.EndTime == null && te.StartTime != null);

        if (entry is null) return NotFound();

        var now = DateTime.UtcNow;
        var duration = (int)(now - entry.StartTime!.Value).TotalSeconds;
        entry.DurationSeconds = Math.Min(duration, 86400);
        entry.EndTime = now;

        await _db.SaveChangesAsync();
        return Ok(ToTimeEntryResponse(entry, entry.Task?.Title));
    }

    [HttpPost("boards/{boardId}/time-entries")]
    public async Task<ActionResult<TimeEntryResponse>> CreateBoardTimeEntry(int boardId, CreateBoardTimeEntryRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var board = await _db.Boards.FirstOrDefaultAsync(b => b.Id == boardId && b.UserId == userId);
        if (board is null) return NotFound();

        TaskItem? task = null;
        if (request.TaskId.HasValue)
        {
            task = await _db.Tasks
                .Include(t => t.Column)
                .FirstOrDefaultAsync(t => t.Id == request.TaskId.Value && t.Column.BoardId == boardId);
            if (task is null) return BadRequest("Task does not belong to this board.");
        }

        var now = DateTime.UtcNow;
        var entry = new TimeEntry
        {
            TaskId = task?.Id,
            BoardId = boardId,
            UserId = userId,
            StartTime = null,
            EndTime = now,
            DurationSeconds = request.DurationMinutes * 60,
            Notes = request.Notes
        };

        _db.TimeEntries.Add(entry);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetBoardTimeEntries), new { boardId }, ToTimeEntryResponse(entry, task?.Title));
    }

    [HttpGet("boards/{boardId}/time-entries")]
    public async Task<ActionResult<List<TimeEntryResponse>>> GetBoardTimeEntries(int boardId, [FromQuery] int take = 20, [FromQuery] int skip = 0)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var board = await _db.Boards.AnyAsync(b => b.Id == boardId && b.UserId == userId);
        if (!board) return NotFound();

        if (take < 1) take = 20;
        if (take > 200) take = 200;
        if (skip < 0) skip = 0;

        // OrderBy splits across nullable columns to avoid coalesce-with-MinValue (MySQL rejects 0001-01-01)
        var entries = await _db.TimeEntries
            .Where(te => te.BoardId == boardId && te.UserId == userId)
            .Include(te => te.Task)
            .OrderByDescending(te => te.EndTime)
            .ThenByDescending(te => te.StartTime)
            .ThenByDescending(te => te.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var response = entries.Select(te => ToTimeEntryResponse(te, te.Task?.Title)).ToList();
        return Ok(response);
    }

    [HttpGet("boards/{boardId}/time-summary")]
    public async Task<ActionResult<BoardTimeSummaryResponse>> GetBoardTimeSummary(int boardId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var board = await _db.Boards.AnyAsync(b => b.Id == boardId && b.UserId == userId);
        if (!board) return NotFound();

        var todayStart = DateTime.Now.Date;
        var weekStart = todayStart.AddDays(-6);
        var todayStartUtc = todayStart.ToUniversalTime();
        var weekStartUtc = weekStart.ToUniversalTime();

        // Mirror the rollup filter used in TasksController.GetTask:
        // include stopped timers (EndTime != null) AND manual entries (StartTime == null);
        // exclude running timers (StartTime != null && EndTime == null).
        // Project raw nullables and coalesce client-side — MySQL rejects DateTime.MinValue in a coalesce chain.
        var rows = await _db.TimeEntries
            .Where(te => te.BoardId == boardId
                      && te.UserId == userId
                      && (te.EndTime != null || te.StartTime == null)
                      && te.DurationSeconds > 0)
            .Select(te => new { te.DurationSeconds, te.EndTime, te.StartTime })
            .ToListAsync();

        var entries = rows
            .Select(r => new { r.DurationSeconds, Stamp = r.EndTime ?? r.StartTime ?? DateTime.UtcNow })
            .ToList();

        var today = entries.Where(e => e.Stamp >= todayStartUtc).ToList();
        var week = entries.Where(e => e.Stamp >= weekStartUtc).ToList();

        return Ok(new BoardTimeSummaryResponse
        {
            BoardId = boardId,
            TodaySeconds = today.Sum(e => e.DurationSeconds),
            WeekSeconds = week.Sum(e => e.DurationSeconds),
            AllTimeSeconds = entries.Sum(e => e.DurationSeconds),
            TodayEntryCount = today.Count,
            WeekEntryCount = week.Count
        });
    }

    // ---------- Update / Delete (entry-id scoped, work for both task + free entries) ----------

    [HttpPut("time-entries/{entryId}")]
    public async Task<IActionResult> UpdateTimeEntry(int entryId, UpdateTimeEntryRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var entry = await _db.TimeEntries
            .FirstOrDefaultAsync(te => te.Id == entryId && te.UserId == userId);

        if (entry is null) return NotFound();

        if (entry.StartTime != null && entry.EndTime == null)
            return BadRequest("Cannot edit a running timer entry. Stop it first.");

        entry.DurationSeconds = request.DurationSeconds;
        entry.Notes = request.Notes;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("time-entries/{entryId}")]
    public async Task<IActionResult> DeleteTimeEntry(int entryId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var entry = await _db.TimeEntries
            .FirstOrDefaultAsync(te => te.Id == entryId && te.UserId == userId);

        if (entry is null) return NotFound();

        if (entry.StartTime != null && entry.EndTime == null)
            return BadRequest("Cannot delete a running timer entry. Stop it first.");

        _db.TimeEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static TimeEntryResponse ToTimeEntryResponse(TimeEntry te, string? taskTitle = null) => new()
    {
        Id = te.Id,
        TaskId = te.TaskId,
        TaskTitle = taskTitle,
        BoardId = te.BoardId,
        StartTime = te.StartTime,
        EndTime = te.EndTime,
        DurationSeconds = te.DurationSeconds,
        Notes = te.Notes,
        IsRunning = te.StartTime != null && te.EndTime == null,
        IsManual = te.StartTime == null
    };
}
