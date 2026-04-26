using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.TimeEntries;

public class CreateTimeEntryRequest
{
    [Required]
    [Range(1, 1440)]
    public int DurationMinutes { get; set; }

    public string? Notes { get; set; }
}
