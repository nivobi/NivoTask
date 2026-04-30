namespace NivoTask.Shared.Dtos.Columns;

public class ColumnResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsDone { get; set; }
    public int? WipLimit { get; set; }
    public int BoardId { get; set; }
}
