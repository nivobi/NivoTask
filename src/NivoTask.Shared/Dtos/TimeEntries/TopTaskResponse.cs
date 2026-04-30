namespace NivoTask.Shared.Dtos.TimeEntries;

public class TopTaskResponse
{
    public int TaskId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public int BoardId { get; set; }
    public string BoardName { get; set; } = string.Empty;
    public string? BoardColor { get; set; }
    public int Seconds { get; set; }
}
