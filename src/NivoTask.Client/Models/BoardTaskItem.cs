using NivoTask.Shared.Dtos.Labels;
using NivoTask.Shared.Dtos.Tasks;

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
    public int? Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public string? CoverColor { get; set; }
    public bool JustCompleted { get; set; }
    public List<LabelResponse> Labels { get; set; } = [];
    public List<BoardSubTaskInfo> SubTasks { get; set; } = [];

    public string FormattedTime
    {
        get
        {
            if (TotalTimeSeconds <= 0) return "";
            var h = TotalTimeSeconds / 3600;
            var m = (TotalTimeSeconds % 3600) / 60;
            if (h > 0) return $"{h}h {m:D2}m";
            if (m > 0) return $"{m}m";
            return $"{TotalTimeSeconds}s";
        }
    }

    public string SubTaskProgress => SubTaskCount > 0
        ? $"{CompletedSubTaskCount}/{SubTaskCount}"
        : "";
}
