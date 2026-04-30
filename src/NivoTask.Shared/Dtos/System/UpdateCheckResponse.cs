namespace NivoTask.Shared.Dtos.System;

public class UpdateCheckResponse
{
    public string CurrentVersion { get; set; } = "0.0.0";
    public string? LatestVersion { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public string? ReleaseUrl { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? AssetName { get; set; }
    public string? AssetUrl { get; set; }
    public long AssetSizeBytes { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? Error { get; set; }
}
