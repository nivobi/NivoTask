namespace NivoTask.Shared.Dtos.Tasks;

public class ActivityEntryResponse
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}
