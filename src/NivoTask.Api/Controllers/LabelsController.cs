using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NivoTask.Api.Data;
using NivoTask.Api.Models;
using Microsoft.AspNetCore.Authorization;
using NivoTask.Shared.Dtos.Labels;

namespace NivoTask.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class LabelsController : ControllerBase
{
    private readonly AppDbContext _db;

    public LabelsController(AppDbContext db) => _db = db;

    [HttpGet("boards/{boardId}/labels")]
    public async Task<ActionResult<List<LabelResponse>>> GetLabels(int boardId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();

        var labels = await _db.Labels
            .Where(l => l.BoardId == boardId)
            .OrderBy(l => l.Name)
            .Select(l => new LabelResponse { Id = l.Id, Name = l.Name, Color = l.Color })
            .ToListAsync();

        return Ok(labels);
    }

    [HttpPost("boards/{boardId}/labels")]
    public async Task<ActionResult<LabelResponse>> CreateLabel(int boardId, CreateLabelRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var board = await _db.Boards
            .Where(b => b.Id == boardId && b.UserId == userId)
            .FirstOrDefaultAsync();

        if (board is null) return NotFound();

        var label = new Label
        {
            Name = request.Name,
            Color = request.Color,
            BoardId = boardId
        };

        _db.Labels.Add(label);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetLabels), new { boardId },
            new LabelResponse { Id = label.Id, Name = label.Name, Color = label.Color });
    }

    [HttpPut("boards/{boardId}/labels/{labelId}")]
    public async Task<IActionResult> UpdateLabel(int boardId, int labelId, UpdateLabelRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var label = await _db.Labels
            .Include(l => l.Board)
            .FirstOrDefaultAsync(l => l.Id == labelId && l.BoardId == boardId && l.Board.UserId == userId);

        if (label is null) return NotFound();

        label.Name = request.Name;
        label.Color = request.Color;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("boards/{boardId}/labels/{labelId}")]
    public async Task<IActionResult> DeleteLabel(int boardId, int labelId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var label = await _db.Labels
            .Include(l => l.Board)
            .FirstOrDefaultAsync(l => l.Id == labelId && l.BoardId == boardId && l.Board.UserId == userId);

        if (label is null) return NotFound();

        _db.Labels.Remove(label);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPut("tasks/{taskId}/labels")]
    public async Task<IActionResult> SetTaskLabels(int taskId, SetTaskLabelsRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var task = await _db.Tasks
            .Include(t => t.TaskLabels)
            .Include(t => t.Column)
            .ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.Column.Board.UserId == userId);

        if (task is null) return NotFound();

        // Verify all label IDs belong to the same board
        var boardId = task.Column.BoardId;
        var validLabelIds = await _db.Labels
            .Where(l => l.BoardId == boardId && request.LabelIds.Contains(l.Id))
            .Select(l => l.Id)
            .ToListAsync();

        // Replace all task labels
        task.TaskLabels.Clear();
        foreach (var labelId in validLabelIds)
        {
            task.TaskLabels.Add(new TaskLabel { TaskId = taskId, LabelId = labelId });
        }

        _db.ActivityEntries.Add(new ActivityEntry
        {
            TaskId = taskId,
            Action = ActivityAction.Updated,
            Detail = $"Labels updated ({validLabelIds.Count} labels)"
        });

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
