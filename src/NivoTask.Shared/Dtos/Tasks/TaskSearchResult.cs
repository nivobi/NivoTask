namespace NivoTask.Shared.Dtos.Tasks;

public class TaskSearchResult
{
    public int TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? ParentTaskId { get; set; }
    public int BoardId { get; set; }
    public string BoardName { get; set; } = string.Empty;
    public string? BoardColor { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}
