namespace NivoTask.Shared.Dtos.TimeEntries;

public class TimeEntryResponse
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationSeconds { get; set; }
    public string? Notes { get; set; }
    public bool IsRunning { get; set; }
    public bool IsManual { get; set; }
}
