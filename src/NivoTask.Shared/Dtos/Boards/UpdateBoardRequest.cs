using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.Boards;

public class UpdateBoardRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Color { get; set; }

    [StringLength(2000)]
    public string? Icon { get; set; }
}
