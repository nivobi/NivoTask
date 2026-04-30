namespace NivoTask.Api.Models;

public class BoardColumn
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsDone { get; set; }
    public int? WipLimit { get; set; }
    public int BoardId { get; set; }
    public Board Board { get; set; } = null!;
    public List<TaskItem> Tasks { get; set; } = [];
}
