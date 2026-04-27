namespace NivoTask.Shared.Dtos.Tasks;

public class BoardTaskResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int ColumnId { get; set; }
    public int SubTaskCount { get; set; }
    public int CompletedSubTaskCount { get; set; }
    public int TotalTimeSeconds { get; set; }
}
