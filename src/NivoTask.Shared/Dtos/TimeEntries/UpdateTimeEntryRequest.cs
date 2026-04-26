using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.TimeEntries;

/// <summary>
/// Request to update a time entry. Duration is in SECONDS (1-86400, i.e. up to 24 hours).
/// Contrast with <see cref="CreateTimeEntryRequest"/> which uses MINUTES.
/// </summary>
public class UpdateTimeEntryRequest
{
    /// <summary>Duration in seconds (1-86400). Create uses minutes; update uses seconds.</summary>
    [Required]
    [Range(1, 86400)]
    public int DurationSeconds { get; set; }

    public string? Notes { get; set; }
}
