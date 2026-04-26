using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.Tasks;

public class ReorderTasksRequest
{
    [Required]
    public List<int> TaskIds { get; set; } = [];
}
