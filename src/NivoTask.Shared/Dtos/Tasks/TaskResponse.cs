namespace NivoTask.Shared.Dtos.Tasks;

public class TaskResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ColumnId { get; set; }
    public int? ParentTaskId { get; set; }
    public int SubTaskCount { get; set; }
}
