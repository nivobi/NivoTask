using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NivoTask.Api.Data;
using NivoTask.Api.Models;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Columns;
using Microsoft.AspNetCore.Authorization;
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
    public async Task<ActionResult<List<BoardSummaryResponse>>> GetBoards()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var boards = await _db.Boards
            .Where(b => b.UserId == userId)
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
                SubTaskCount = t.SubTasks.Count,
                CompletedSubTaskCount = t.SubTasks.Count(s => s.Column.IsDone)
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
        Columns = board.Columns
            .OrderBy(c => c.SortOrder)
            .Select(c => new ColumnResponse
            {
                Id = c.Id,
                Name = c.Name,
                SortOrder = c.SortOrder,
                IsDone = c.IsDone,
                BoardId = c.BoardId
            })
            .ToList()
    };
}
