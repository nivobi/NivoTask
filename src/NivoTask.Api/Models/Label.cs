namespace NivoTask.Api.Models;

public class Label
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int BoardId { get; set; }
    public Board Board { get; set; } = null!;
    public List<TaskLabel> TaskLabels { get; set; } = [];
}
