using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NivoTask.Api.Data;
using NivoTask.Api.Models;
using Microsoft.AspNetCore.Authorization;
using NivoTask.Shared.Dtos.Labels;
using NivoTask.Shared.Dtos.Tasks;

namespace NivoTask.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly AppDbContext _db;
    private const int SortGap = 1000;

    public TasksController(AppDbContext db) => _db = db;

    [HttpPost("boards/{boardId}/columns/{columnId}/tasks")]
    public async Task<ActionResult<TaskResponse>> CreateTask(int boardId, int columnId, CreateTaskRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var column = await _db.BoardColumns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == columnId && c.BoardId == boardId && c.Board.UserId == userId);

        if (column is null) return NotFound();

        var maxSort = await _db.Tasks
            .Where(t => t.ColumnId == columnId && t.ParentTaskId == null)
            .MaxAsync(t => (int?)t.SortOrder) ?? 0;

        var task = new TaskItem
        {
            Title = request.Title,
            Description = request.Description,
            Priority = request.Priority,
            DueDate = request.DueDate,
            CoverColor = request.CoverColor,
            ColumnId = columnId,
            ParentTaskId = null,
            SortOrder = maxSort + SortGap
        };

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        LogActivity(task.Id, ActivityAction.Created, $"Created \"{task.Title}\"");
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTask), new { taskId = task.Id }, ToTaskResponse(task));
    }

    [HttpGet("tasks/{taskId}")]
    public async Task<ActionResult<TaskDetailResponse>> GetTask(int taskId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var task = await _db.Tasks
            .Include(t => t.SubTasks.OrderBy(s => s.SortOrder))
            .Include(t => t.TaskLabels).ThenInclude(tl => tl.Label)
            .Include(t => t.Column)
            .ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.Column.Board.UserId == userId);

        if (task is null) return NotFound();

        // Hierarchical rollup: sum completed entries for this task + all its sub-tasks (D-04, D-05)
        // Include: stopped timers (EndTime != null) AND manual entries (StartTime == null)
        // Exclude: running timers (StartTime != null && EndTime == null)
        var totalTimeSeconds = await _db.TimeEntries
            .Where(te => (te.TaskId == taskId || te.Task.ParentTaskId == taskId)
                      && (te.EndTime != null || te.StartTime == null)
                      && te.DurationSeconds > 0)
            .SumAsync(te => te.DurationSeconds);

        return Ok(ToTaskDetailResponse(task, totalTimeSeconds));
    }

    [HttpPut("tasks/{taskId}")]
    public async Task<IActionResult> UpdateTask(int taskId, UpdateTaskRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var task = await _db.Tasks
            .Include(t => t.Column)
            .ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.Column.Board.UserId == userId);

        if (task is null) return NotFound();

        // Track changes for activity log
        var changes = new List<string>();
        if (task.Title != request.Title) changes.Add("title");
        if (task.Description != request.Description) changes.Add("description");
        if (task.Priority != request.Priority) changes.Add("priority");
        if (task.DueDate != request.DueDate) changes.Add("due date");
        if (task.CoverColor != request.CoverColor) changes.Add("cover color");

        task.Title = request.Title;
        task.Description = request.Description;
        task.Priority = request.Priority;
        task.DueDate = request.DueDate;
        task.CoverColor = request.CoverColor;

        if (changes.Count > 0)
            LogActivity(taskId, ActivityAction.Updated, $"Updated {string.Join(", ", changes)}");

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("tasks/{taskId}/done")]
    public async Task<IActionResult> ToggleTaskDone(int taskId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var task = await _db.Tasks
            .Include(t => t.Column)
            .ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.Column.Board.UserId == userId);

        if (task is null) return NotFound();

        task.IsDone = !task.IsDone;
        LogActivity(taskId, task.IsDone ? ActivityAction.Completed : ActivityAction.Updated,
            task.IsDone ? "Marked as done" : "Marked as not done");
        await _db.SaveChangesAsync();
        return Ok(new { task.IsDone });
    }

    [HttpDelete("tasks/{taskId}")]
    public async Task<IActionResult> DeleteTask(int taskId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var task = await _db.Tasks
            .Include(t => t.Column)
            .ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.Column.Board.UserId == userId);

        if (task is null) return NotFound();

        _db.Tasks.Remove(task);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("tasks/{taskId}/subtasks")]
    public async Task<ActionResult<TaskResponse>> CreateSubTask(int taskId, CreateTaskRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var parentTask = await _db.Tasks
            .Include(t => t.Column)
            .ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.ParentTaskId == null && t.Column.Board.UserId == userId);

        if (parentTask is null) return NotFound();

        var maxSort = await _db.Tasks
            .Where(t => t.ParentTaskId == taskId)
            .MaxAsync(t => (int?)t.SortOrder) ?? 0;

        var subTask = new TaskItem
        {
            Title = request.Title,
            Description = request.Description,
            ColumnId = parentTask.ColumnId,
            ParentTaskId = taskId,
            SortOrder = maxSort + SortGap
        };

        _db.Tasks.Add(subTask);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTask), new { taskId = subTask.Id }, ToTaskResponse(subTask));
    }

    [HttpPatch("tasks/{taskId}/move")]
    public async Task<IActionResult> MoveTask(int taskId, MoveTaskRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var task = await _db.Tasks
            .Include(t => t.SubTasks)
            .Include(t => t.Column)
            .ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.ParentTaskId == null && t.Column.Board.UserId == userId);

        if (task is null) return NotFound();

        // Verify target column belongs to same board
        var targetColumnExists = await _db.BoardColumns
            .AnyAsync(c => c.Id == request.TargetColumnId && c.BoardId == task.Column.BoardId);

        if (!targetColumnExists) return BadRequest("Target column not found in this board");

        var fromColumnName = task.Column.Name;
        task.ColumnId = request.TargetColumnId;
        task.SortOrder = request.NewSortOrder;

        // Log move
        var targetColumn = await _db.BoardColumns.FindAsync(request.TargetColumnId);
        LogActivity(taskId, ActivityAction.Moved, $"Moved from \"{fromColumnName}\" to \"{targetColumn?.Name}\"");

        // D-07: Sync sub-tasks to parent's new column
        foreach (var sub in task.SubTasks)
        {
            sub.ColumnId = request.TargetColumnId;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("boards/{boardId}/columns/{columnId}/tasks/reorder")]
    public async Task<IActionResult> ReorderTasks(int boardId, int columnId, ReorderTasksRequest request)
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

        var headTasks = await _db.Tasks
            .Where(t => t.ColumnId == columnId && t.ParentTaskId == null)
            .ToListAsync();

        // Validate: all IDs must match the column's head tasks
        if (request.TaskIds.Count != headTasks.Count)
        {
            return BadRequest("Task ID count does not match the number of head tasks in this column");
        }

        var taskDict = headTasks.ToDictionary(t => t.Id);
        foreach (var id in request.TaskIds)
        {
            if (!taskDict.ContainsKey(id))
            {
                return BadRequest($"Task ID {id} does not belong to this column");
            }
        }

        // Re-normalize SortOrder based on request order
        for (int i = 0; i < request.TaskIds.Count; i++)
        {
            taskDict[request.TaskIds[i]].SortOrder = (i + 1) * SortGap;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static TaskResponse ToTaskResponse(TaskItem t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Description = t.Description,
        SortOrder = t.SortOrder,
        CreatedAt = t.CreatedAt,
        ColumnId = t.ColumnId,
        ParentTaskId = t.ParentTaskId,
        SubTaskCount = t.SubTasks?.Count ?? 0,
        IsDone = t.IsDone,
        Priority = t.Priority,
        DueDate = t.DueDate,
        CoverColor = t.CoverColor
    };

    private static TaskDetailResponse ToTaskDetailResponse(TaskItem t, int totalTimeSeconds = 0) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Description = t.Description,
        SortOrder = t.SortOrder,
        CreatedAt = t.CreatedAt,
        ColumnId = t.ColumnId,
        ParentTaskId = t.ParentTaskId,
        Priority = t.Priority,
        DueDate = t.DueDate,
        CoverColor = t.CoverColor,
        Labels = t.TaskLabels?.Select(tl => new LabelResponse
        {
            Id = tl.Label.Id,
            Name = tl.Label.Name,
            Color = tl.Label.Color
        }).ToList() ?? [],
        SubTasks = t.SubTasks?
            .OrderBy(s => s.SortOrder)
            .Select(s => ToTaskResponse(s))
            .ToList() ?? [],
        TotalTimeSeconds = totalTimeSeconds
    };

    [HttpGet("tasks/{taskId}/activity")]
    public async Task<ActionResult<List<ActivityEntryResponse>>> GetActivity(int taskId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var taskExists = await _db.Tasks
            .Include(t => t.Column).ThenInclude(c => c.Board)
            .AnyAsync(t => t.Id == taskId && t.Column.Board.UserId == userId);

        if (!taskExists) return NotFound();

        var entries = await _db.ActivityEntries
            .Where(a => a.TaskId == taskId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .Select(a => new ActivityEntryResponse
            {
                Id = a.Id,
                Action = a.Action.ToString(),
                Detail = a.Detail,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return Ok(entries);
    }

    private void LogActivity(int taskId, ActivityAction action, string? detail = null)
    {
        _db.ActivityEntries.Add(new ActivityEntry
        {
            TaskId = taskId,
            Action = action,
            Detail = detail
        });
    }
}
