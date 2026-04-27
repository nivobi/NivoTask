using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NivoTask.Api.Data;
using NivoTask.Api.Models;
using Microsoft.AspNetCore.Authorization;
using NivoTask.Shared.Dtos.Columns;

namespace NivoTask.Api.Controllers;

[ApiController]
[Route("api/boards/{boardId}/columns")]
[Authorize]
public class ColumnsController : ControllerBase
{
    private readonly AppDbContext _db;
    private const int SortGap = 1000;

    public ColumnsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<ColumnResponse>>> GetColumns(int boardId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();

        var columns = await _db.BoardColumns
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.SortOrder)
            .Select(c => ToColumnResponse(c))
            .ToListAsync();

        return Ok(columns);
    }

    [HttpPost]
    public async Task<ActionResult<ColumnResponse>> CreateColumn(int boardId, CreateColumnRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();

        // IsDone constraint: only one column per board can have IsDone=true
        if (request.IsDone)
        {
            var existingDoneColumns = await _db.BoardColumns
                .Where(c => c.BoardId == boardId && c.IsDone)
                .ToListAsync();

            foreach (var col in existingDoneColumns)
            {
                col.IsDone = false;
            }
        }

        var maxSort = await _db.BoardColumns
            .Where(c => c.BoardId == boardId)
            .MaxAsync(c => (int?)c.SortOrder) ?? 0;

        var column = new BoardColumn
        {
            Name = request.Name,
            IsDone = request.IsDone,
            SortOrder = maxSort + SortGap,
            BoardId = boardId
        };

        _db.BoardColumns.Add(column);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetColumns), new { boardId }, ToColumnResponse(column));
    }

    [HttpPut("{columnId}")]
    public async Task<IActionResult> UpdateColumn(int boardId, int columnId, UpdateColumnRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();

        var column = await _db.BoardColumns
            .Where(c => c.Id == columnId && c.BoardId == boardId)
            .FirstOrDefaultAsync();

        if (column is null) return NotFound();

        // IsDone constraint: if setting IsDone=true and it wasn't already, clear others
        if (request.IsDone && !column.IsDone)
        {
            var otherDoneColumns = await _db.BoardColumns
                .Where(c => c.BoardId == boardId && c.IsDone && c.Id != columnId)
                .ToListAsync();

            foreach (var col in otherDoneColumns)
            {
                col.IsDone = false;
            }
        }

        column.Name = request.Name;
        column.IsDone = request.IsDone;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{columnId}")]
    public async Task<IActionResult> DeleteColumn(int boardId, int columnId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();

        var column = await _db.BoardColumns
            .Where(c => c.Id == columnId && c.BoardId == boardId)
            .FirstOrDefaultAsync();

        if (column is null) return NotFound();

        // Minimum 1 column per board
        var columnCount = await _db.BoardColumns.CountAsync(c => c.BoardId == boardId);
        if (columnCount <= 1)
        {
            return BadRequest("Cannot delete the last column on a board");
        }

        // Block delete if column has tasks (D-14)
        var hasTasks = await _db.Tasks.AnyAsync(t => t.ColumnId == columnId);
        if (hasTasks)
        {
            return BadRequest("Move tasks to another column first");
        }

        _db.BoardColumns.Remove(column);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("reorder")]
    public async Task<IActionResult> ReorderColumns(int boardId, ReorderColumnsRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();

        var columns = await _db.BoardColumns
            .Where(c => c.BoardId == boardId)
            .ToListAsync();

        // Validate: all IDs must belong to this board and count must match
        if (request.ColumnIds.Count != columns.Count)
        {
            return BadRequest("Column ID count does not match the number of columns on this board");
        }

        var columnDict = columns.ToDictionary(c => c.Id);
        foreach (var id in request.ColumnIds)
        {
            if (!columnDict.ContainsKey(id))
            {
                return BadRequest($"Column ID {id} does not belong to this board");
            }
        }

        // Re-normalize SortOrder based on request order
        for (int i = 0; i < request.ColumnIds.Count; i++)
        {
            columnDict[request.ColumnIds[i]].SortOrder = (i + 1) * SortGap;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static ColumnResponse ToColumnResponse(BoardColumn c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        SortOrder = c.SortOrder,
        IsDone = c.IsDone,
        BoardId = c.BoardId
    };
}
