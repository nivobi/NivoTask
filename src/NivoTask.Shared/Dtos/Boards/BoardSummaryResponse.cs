namespace NivoTask.Shared.Dtos.Boards;

public class BoardSummaryResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public int ColumnCount { get; set; }
    public int TaskCount { get; set; }
}
