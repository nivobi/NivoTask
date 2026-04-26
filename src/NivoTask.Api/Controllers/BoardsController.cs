using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NivoTask.Api.Data;
using NivoTask.Api.Models;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Columns;

namespace NivoTask.Api.Controllers;

[ApiController]
[Route("api/boards")]
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
