using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.Tasks;

public class UpdateTaskRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }
    public int? Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public string? CoverColor { get; set; }
}
