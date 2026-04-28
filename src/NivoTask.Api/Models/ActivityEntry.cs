namespace NivoTask.Api.Models;

public class ActivityEntry
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public ActivityAction Action { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ActivityAction
{
    Created,
    Updated,
    Moved,
    LabelAdded,
    LabelRemoved,
    PriorityChanged,
    DueDateSet,
    Completed,
    DescriptionUpdated,
    CoverColorChanged
}
