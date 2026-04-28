using NivoTask.Shared.Dtos.Columns;

namespace NivoTask.Shared.Dtos.Boards;

public class BoardResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? BackgroundType { get; set; }
    public string? BackgroundValue { get; set; }
    public List<ColumnResponse> Columns { get; set; } = [];
}
