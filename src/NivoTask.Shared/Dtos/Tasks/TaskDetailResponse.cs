using NivoTask.Shared.Dtos.Labels;

namespace NivoTask.Shared.Dtos.Tasks;

public class TaskDetailResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ColumnId { get; set; }
    public int? ParentTaskId { get; set; }
    public int? Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public string? CoverColor { get; set; }
    public List<LabelResponse> Labels { get; set; } = [];
    public List<TaskResponse> SubTasks { get; set; } = [];
    public int TotalTimeSeconds { get; set; }
}
