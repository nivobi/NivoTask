namespace NivoTask.Client.Models;

public class BoardTaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string ColumnIdentifier { get; set; } = string.Empty;
    public int SubTaskCount { get; set; }
    public int CompletedSubTaskCount { get; set; }
    public int TotalTimeSeconds { get; set; }

    public string FormattedTime => TotalTimeSeconds > 0
        ? $"{TotalTimeSeconds / 3600}h {(TotalTimeSeconds % 3600) / 60}m"
        : "";

    public string SubTaskProgress => SubTaskCount > 0
        ? $"{CompletedSubTaskCount}/{SubTaskCount} sub-tasks"
        : "";
}
