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
            .FirstOrDefaultAsync(te => te.UserId == userId && te.EndTime == null && te.StartTime != null);

        if (activeEntry is not null)
        {
            return Conflict(new ActiveTimerResponse
            {
                EntryId = activeEntry.Id,
                TaskId = activeEntry.TaskId,
                TaskTitle = activeEntry.Task.Title,
                ElapsedSeconds = Math.Max(0, (int)(DateTime.UtcNow - activeEntry.StartTime!.Value).TotalSeconds),
                StartTime = activeEntry.StartTime!.Value
            });
        }

        var entry = new TimeEntry
        {
            TaskId = taskId,
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
                // Retry the insert after cleaning ghosts
                _db.TimeEntries.Add(new TimeEntry
                {
                    TaskId = taskId, UserId = userId,
                    StartTime = DateTime.UtcNow, EndTime = null, DurationSeconds = 0
                });
                try
                {
                    await _db.SaveChangesAsync();
                    return CreatedAtAction(nameof(GetTimeEntries), new { taskId }, ToTimeEntryResponse(entry));
                }
                catch { }
            }
            // Genuine conflict — another timer was started concurrently
            return Conflict(new { message = "Another timer was started concurrently." });
        }

        return CreatedAtAction(nameof(GetTimeEntries), new { taskId }, ToTimeEntryResponse(entry));
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

        // Find active timer on this specific task (validates taskId match per Pitfall 3)
        var entry = await _db.TimeEntries
            .FirstOrDefaultAsync(te => te.UserId == userId && te.TaskId == taskId && te.EndTime == null && te.StartTime != null);

        if (entry is null) return NotFound();

        // Server-computed duration (D-03), capped at 86400 seconds (Pitfall 4)
        // Capture UtcNow once so EndTime and DurationSeconds are consistent
        var now = DateTime.UtcNow;
        var duration = (int)(now - entry.StartTime!.Value).TotalSeconds;
        entry.DurationSeconds = Math.Min(duration, 86400);
        entry.EndTime = now;

        await _db.SaveChangesAsync();
        return Ok(ToTimeEntryResponse(entry));
    }

    // TODO: Implement stale-timer detection. If a timer runs for > 24h (orphaned due to
    // browser close / app crash), it will never contribute to rollup because DurationSeconds
    // stays 0 until StopTimer is called. Consider a background job or UI warning when
    // ElapsedSeconds > 86400. See WR-03 in phase 03 code review.
    [HttpGet("timer/active")]
    public async Task<IActionResult> GetActiveTimer()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var entry = await _db.TimeEntries
            .Include(te => te.Task)
            .FirstOrDefaultAsync(te => te.UserId == userId && te.EndTime == null && te.StartTime != null);

        if (entry is null) return NoContent();

        return Ok(new ActiveTimerResponse
        {
            EntryId = entry.Id,
            TaskId = entry.TaskId,
            TaskTitle = entry.Task.Title,
            ElapsedSeconds = Math.Max(0, (int)(DateTime.UtcNow - entry.StartTime!.Value).TotalSeconds),
            StartTime = entry.StartTime!.Value
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

        var response = entries.Select(te => ToTimeEntryResponse(te)).ToList();

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

        // Manual entries must have EndTime set (not null) to avoid conflicting
        // with the partial unique index on EndTime IS NULL (active timer guard)
        var now = DateTime.UtcNow;
        var entry = new TimeEntry
        {
            TaskId = taskId,
            UserId = userId,
            StartTime = null,
            EndTime = now,
            DurationSeconds = request.DurationMinutes * 60,
            Notes = request.Notes
        };

        _db.TimeEntries.Add(entry);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTimeEntries), new { taskId }, ToTimeEntryResponse(entry));
    }

    [HttpPut("time-entries/{entryId}")]
    public async Task<IActionResult> UpdateTimeEntry(int entryId, UpdateTimeEntryRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var entry = await _db.TimeEntries
            .Include(te => te.Task).ThenInclude(t => t.Column).ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(te => te.Id == entryId && te.Task.Column.Board.UserId == userId);

        if (entry is null) return NotFound();

        // Reject editing a running timer (T-03-10)
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
            .Include(te => te.Task).ThenInclude(t => t.Column).ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(te => te.Id == entryId && te.Task.Column.Board.UserId == userId);

        if (entry is null) return NotFound();

        // Reject deleting a running timer (T-03-10)
        if (entry.StartTime != null && entry.EndTime == null)
            return BadRequest("Cannot delete a running timer entry. Stop it first.");

        _db.TimeEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static TimeEntryResponse ToTimeEntryResponse(TimeEntry te) => new()
    {
        Id = te.Id,
        TaskId = te.TaskId,
        StartTime = te.StartTime,
        EndTime = te.EndTime,
        DurationSeconds = te.DurationSeconds,
        Notes = te.Notes,
        IsRunning = te.StartTime != null && te.EndTime == null,
        IsManual = te.StartTime == null
    };
}
