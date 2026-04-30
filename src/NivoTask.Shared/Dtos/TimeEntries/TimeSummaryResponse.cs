namespace NivoTask.Shared.Dtos.TimeEntries;

public class TimeSummaryResponse
{
    public int TodaySeconds { get; set; }
    public int WeekSeconds { get; set; }
    public int PreviousWeekSeconds { get; set; }
    public int TodayEntryCount { get; set; }
    public int WeekEntryCount { get; set; }
}
