using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.Tasks;

public class MoveTaskRequest
{
    [Required]
    public int TargetColumnId { get; set; }

    [Required]
    public int NewSortOrder { get; set; }
}
