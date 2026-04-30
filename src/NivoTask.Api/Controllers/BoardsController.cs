using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NivoTask.Api.Data;
using NivoTask.Api.Models;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Columns;
using Microsoft.AspNetCore.Authorization;
using NivoTask.Shared.Dtos.Labels;
using NivoTask.Shared.Dtos.Tasks;

namespace NivoTask.Api.Controllers;

[ApiController]
[Route("api/boards")]
[Authorize]
public class BoardsController : ControllerBase
{
    private readonly AppDbContext _db;

    public BoardsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<BoardSummaryResponse>>> GetBoards([FromQuery] string? archived = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var query = _db.Boards.Where(b => b.UserId == userId);
        if (string.Equals(archived, "only", StringComparison.OrdinalIgnoreCase))
            query = query.Where(b => b.IsArchived);
        else if (!string.Equals(archived, "true", StringComparison.OrdinalIgnoreCase))
            query = query.Where(b => !b.IsArchived);

        var boards = await query
            .OrderBy(b => b.Name)
            .Select(b => new BoardSummaryResponse
            {
                Id = b.Id,
                Name = b.Name,
                Color = b.Color,
                Icon = b.Icon,
                ColumnCount = b.Columns.Count,
                TaskCount = b.Columns.SelectMany(c => c.Tasks).Count()
            })
            .ToListAsync();

        if (boards.Count == 0) return Ok(boards);

        var todayStart = DateTime.Now.Date;
        var weekStart = todayStart.AddDays(-6);
        var todayStartUtc = todayStart.ToUniversalTime();
        var weekStartUtc = weekStart.ToUniversalTime();
        var boardIds = boards.Select(b => b.Id).ToList();

        // Mirror rollup filter from GetBoardTimeSummary: stopped timers OR manual entries.
        var rows = await _db.TimeEntries
            .Where(te => te.UserId == userId
                      && boardIds.Contains(te.BoardId)
                      && (te.EndTime != null || te.StartTime == null)
                      && te.DurationSeconds > 0)
            .Select(te => new { te.BoardId, te.DurationSeconds, te.EndTime, te.StartTime })
            .ToListAsync();

        var stamped = rows
            .Select(r => new { r.BoardId, r.DurationSeconds, Stamp = r.EndTime ?? r.StartTime ?? DateTime.UtcNow })
            .ToList();

        var weekByBoard = stamped
            .Where(x => x.Stamp >= weekStartUtc)
            .GroupBy(x => x.BoardId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.DurationSeconds));

        var todayByBoard = stamped
            .Where(x => x.Stamp >= todayStartUtc)
            .GroupBy(x => x.BoardId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.DurationSeconds));

        foreach (var b in boards)
        {
            b.TodaySeconds = todayByBoard.TryGetValue(b.Id, out var t) ? t : 0;
            b.WeekSeconds = weekByBoard.TryGetValue(b.Id, out var w) ? w : 0;
        }

        return Ok(boards);
    }

    [HttpGet("{boardId}")]
    public async Task<ActionResult<BoardResponse>> GetBoard(int boardId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Include(b => b.Columns.OrderBy(c => c.SortOrder))
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();

        return Ok(ToBoardResponse(board));
    }

    [HttpGet("{boardId}/tasks")]
    public async Task<ActionResult<List<BoardTaskResponse>>> GetBoardTasks(int boardId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();

        var columnIds = await _db.BoardColumns
            .Where(c => c.BoardId == boardId)
            .Select(c => c.Id)
            .ToListAsync();

        var headTasks = await _db.Tasks
            .Where(t => columnIds.Contains(t.ColumnId) && t.ParentTaskId == null)
            .OrderBy(t => t.SortOrder)
            .Select(t => new BoardTaskResponse
            {
                Id = t.Id,
                Title = t.Title,
                SortOrder = t.SortOrder,
                ColumnId = t.ColumnId,
                IsDone = t.IsDone,
                SubTaskCount = t.SubTasks.Count,
                CompletedSubTaskCount = t.SubTasks.Count(s => s.IsDone),
                Priority = t.Priority,
                DueDate = t.DueDate,
                CoverColor = t.CoverColor,
                Labels = t.TaskLabels.Select(tl => new LabelResponse
                {
                    Id = tl.Label.Id,
                    Name = tl.Label.Name,
                    Color = tl.Label.Color
                }).ToList(),
                SubTasks = t.SubTasks.OrderBy(s => s.SortOrder).Select(s => new BoardSubTaskInfo
                {
                    Id = s.Id,
                    Title = s.Title,
                    IsDone = s.IsDone
                }).ToList()
            })
            .ToListAsync();

        // Compute time rollup per task (same pattern as TasksController.GetTask)
        // Include: stopped timers (EndTime != null) AND manual entries (StartTime == null)
        // Exclude: running timers (StartTime != null && EndTime == null)
        foreach (var task in headTasks)
        {
            task.TotalTimeSeconds = await _db.TimeEntries
                .Where(te => (te.TaskId == task.Id || te.Task.ParentTaskId == task.Id)
                          && (te.EndTime != null || te.StartTime == null)
                          && te.DurationSeconds > 0)
                .SumAsync(te => te.DurationSeconds);
        }

        return Ok(headTasks);
    }

    [HttpPost]
    public async Task<ActionResult<BoardResponse>> CreateBoard(CreateBoardRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = new Board
        {
            Name = request.Name,
            Color = request.Color,
            Icon = request.Icon,
            UserId = userId!,
            Columns =
            [
                new BoardColumn { Name = "To Do", SortOrder = 1000, IsDone = false },
                new BoardColumn { Name = "In Progress", SortOrder = 2000, IsDone = false },
                new BoardColumn { Name = "Done", SortOrder = 3000, IsDone = true }
            ]
        };

        _db.Boards.Add(board);
        await _db.SaveChangesAsync();

        var response = ToBoardResponse(board);
        return CreatedAtAction(nameof(GetBoard), new { boardId = board.Id }, response);
    }

    [HttpPut("{boardId}")]
    public async Task<IActionResult> UpdateBoard(int boardId, UpdateBoardRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();

        board.Name = request.Name;
        board.Color = request.Color;
        board.Icon = request.Icon;
        board.BackgroundType = request.BackgroundType;
        board.BackgroundValue = request.BackgroundValue;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{boardId}/duplicate")]
    public async Task<ActionResult<BoardResponse>> DuplicateBoard(int boardId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var src = await _db.Boards
            .Include(b => b.Columns).ThenInclude(c => c.Tasks).ThenInclude(t => t.SubTasks)
            .Include(b => b.Columns).ThenInclude(c => c.Tasks).ThenInclude(t => t.TaskLabels)
            .Include(b => b.Labels)
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (src is null) return NotFound();

        var copy = new Board
        {
            Name = src.Name + " (Copy)",
            Color = src.Color,
            Icon = src.Icon,
            BackgroundType = src.BackgroundType,
            BackgroundValue = src.BackgroundValue,
            UserId = userId!
        };
        _db.Boards.Add(copy);
        await _db.SaveChangesAsync();

        // Clone labels and remember mapping
        var labelMap = new Dictionary<int, int>();
        foreach (var lbl in src.Labels)
        {
            var l = new Label { Name = lbl.Name, Color = lbl.Color, BoardId = copy.Id };
            _db.Labels.Add(l);
            await _db.SaveChangesAsync();
            labelMap[lbl.Id] = l.Id;
        }

        // Clone columns + head tasks + sub-tasks + label associations
        foreach (var col in src.Columns.OrderBy(c => c.SortOrder))
        {
            var newCol = new BoardColumn
            {
                Name = col.Name,
                SortOrder = col.SortOrder,
                IsDone = col.IsDone,
                WipLimit = col.WipLimit,
                BoardId = copy.Id
            };
            _db.BoardColumns.Add(newCol);
            await _db.SaveChangesAsync();

            foreach (var t in col.Tasks.Where(x => x.ParentTaskId == null).OrderBy(x => x.SortOrder))
            {
                var newTask = new TaskItem
                {
                    Title = t.Title,
                    Description = t.Description,
                    Priority = t.Priority,
                    DueDate = t.DueDate,
                    CoverColor = t.CoverColor,
                    SortOrder = t.SortOrder,
                    ColumnId = newCol.Id,
                    ParentTaskId = null
                };
                _db.Tasks.Add(newTask);
                await _db.SaveChangesAsync();

                foreach (var sub in t.SubTasks.OrderBy(s => s.SortOrder))
                {
                    _db.Tasks.Add(new TaskItem
                    {
                        Title = sub.Title,
                        Description = sub.Description,
                        Priority = sub.Priority,
                        DueDate = sub.DueDate,
                        CoverColor = sub.CoverColor,
                        SortOrder = sub.SortOrder,
                        ColumnId = newCol.Id,
                        ParentTaskId = newTask.Id
                    });
                }

                foreach (var tl in t.TaskLabels)
                {
                    if (labelMap.TryGetValue(tl.LabelId, out var newLabelId))
                        _db.TaskLabels.Add(new TaskLabel { TaskId = newTask.Id, LabelId = newLabelId });
                }
            }
        }

        await _db.SaveChangesAsync();

        var result = await _db.Boards
            .Include(b => b.Columns.OrderBy(c => c.SortOrder))
            .FirstOrDefaultAsync(b => b.Id == copy.Id);
        return Ok(ToBoardResponse(result!));
    }

    [HttpPost("{boardId}/archive")]
    public async Task<IActionResult> ArchiveBoard(int boardId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();
        if (board.IsArchived) return NoContent();

        board.IsArchived = true;
        board.ArchivedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{boardId}/unarchive")]
    public async Task<IActionResult> UnarchiveBoard(int boardId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();
        if (!board.IsArchived) return NoContent();

        board.IsArchived = false;
        board.ArchivedAt = null;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{boardId}")]
    public async Task<IActionResult> DeleteBoard(int boardId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();

        _db.Boards.Remove(board);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static BoardResponse ToBoardResponse(Board board) => new()
    {
        Id = board.Id,
        Name = board.Name,
        Color = board.Color,
        Icon = board.Icon,
        CreatedAt = board.CreatedAt,
        BackgroundType = board.BackgroundType,
        BackgroundValue = board.BackgroundValue,
        Columns = board.Columns
            .OrderBy(c => c.SortOrder)
            .Select(c => new ColumnResponse
            {
                Id = c.Id,
                Name = c.Name,
                SortOrder = c.SortOrder,
                IsDone = c.IsDone,
                WipLimit = c.WipLimit,
                BoardId = c.BoardId
            })
            .ToList()
    };
}
