namespace NivoTask.Shared.Dtos.TimeEntries;

public class ActiveTimerResponse
{
    public int EntryId { get; set; }
    public int? TaskId { get; set; }
    public string? TaskTitle { get; set; }
    public int BoardId { get; set; }
    public string BoardName { get; set; } = string.Empty;
    public int ElapsedSeconds { get; set; }
    public DateTime StartTime { get; set; }
}
