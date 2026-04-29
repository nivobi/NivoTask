namespace NivoTask.Shared.Dtos.TimeEntries;

public class BoardTimeSummaryResponse
{
    public int BoardId { get; set; }
    public int TodaySeconds { get; set; }
    public int WeekSeconds { get; set; }
    public int AllTimeSeconds { get; set; }
    public int TodayEntryCount { get; set; }
    public int WeekEntryCount { get; set; }
}
