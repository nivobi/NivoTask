namespace NivoTask.Api.Models;

public class TimeEntry
{
    public int Id { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationSeconds { get; set; }
    public string? Notes { get; set; }
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;
}
