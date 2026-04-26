using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.TimeEntries;

/// <summary>
/// Request to create a manual time entry. Duration is in MINUTES (converted to seconds server-side).
/// Contrast with <see cref="UpdateTimeEntryRequest"/> which uses SECONDS.
/// </summary>
public class CreateTimeEntryRequest
{
    /// <summary>Duration in minutes (1-1440, i.e. up to 24 hours). Stored as seconds server-side.</summary>
    [Required]
    [Range(1, 1440)]
    public int DurationMinutes { get; set; }

    public string? Notes { get; set; }
}
