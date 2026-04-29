using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.TimeEntries;

/// <summary>
/// Request to create a manual board-scoped time entry. Duration is in MINUTES.
/// TaskId is optional — if null, the entry is a free board-level entry.
/// </summary>
public class CreateBoardTimeEntryRequest
{
    public int? TaskId { get; set; }

    [Required]
    [Range(1, 1440)]
    public int DurationMinutes { get; set; }

    public string? Notes { get; set; }
}
