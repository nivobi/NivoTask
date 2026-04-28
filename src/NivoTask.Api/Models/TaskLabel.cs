namespace NivoTask.Api.Models;

public class TaskLabel
{
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public int LabelId { get; set; }
    public Label Label { get; set; } = null!;
}
