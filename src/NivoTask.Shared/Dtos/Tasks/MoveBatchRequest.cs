namespace NivoTask.Shared.Dtos.Tasks;

public class MoveBatchRequest
{
    public List<int> TaskIds { get; set; } = [];
    public int TargetColumnId { get; set; }
}
