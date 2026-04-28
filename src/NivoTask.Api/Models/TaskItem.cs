namespace NivoTask.Api.Models;

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int ColumnId { get; set; }
    public BoardColumn Column { get; set; } = null!;
    public bool IsDone { get; set; }
    public int? Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public string? CoverColor { get; set; }
    public int? ParentTaskId { get; set; }
    public TaskItem? ParentTask { get; set; }
    public List<TaskItem> SubTasks { get; set; } = [];
    public List<TimeEntry> TimeEntries { get; set; } = [];
    public List<TaskLabel> TaskLabels { get; set; } = [];
}
