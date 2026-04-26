using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.Columns;

public class CreateColumnRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public bool IsDone { get; set; }
}
