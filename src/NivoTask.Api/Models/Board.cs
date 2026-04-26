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
    public List<BoardColumn> Columns { get; set; } = [];
}
