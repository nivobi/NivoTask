namespace NivoTask.Api.Models;

public class Board
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;
    public int? BackgroundType { get; set; }
    public string? BackgroundValue { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public List<BoardColumn> Columns { get; set; } = [];
    public List<Label> Labels { get; set; } = [];
}
