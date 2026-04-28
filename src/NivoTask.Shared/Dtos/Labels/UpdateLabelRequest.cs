using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.Labels;

public class UpdateLabelRequest
{
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Color { get; set; } = string.Empty;
}
