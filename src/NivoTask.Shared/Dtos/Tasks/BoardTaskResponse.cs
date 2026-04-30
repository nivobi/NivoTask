using NivoTask.Shared.Dtos.Labels;

namespace NivoTask.Shared.Dtos.Tasks;

public class BoardTaskResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int ColumnId { get; set; }
    public bool IsDone { get; set; }
    public int SubTaskCount { get; set; }
    public int CompletedSubTaskCount { get; set; }
    public int TotalTimeSeconds { get; set; }
    public int? Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public string? CoverColor { get; set; }
    public List<LabelResponse> Labels { get; set; } = [];
    public List<BoardSubTaskInfo> SubTasks { get; set; } = [];
}

public class BoardSubTaskInfo
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}
