using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.TimeEntries;

public class UpdateTimeEntryRequest
{
    [Required]
    [Range(1, 86400)]
    public int DurationSeconds { get; set; }

    public string? Notes { get; set; }
}
