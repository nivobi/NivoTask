namespace NivoTask.Shared.Dtos.System;

public class VersionInfoResponse
{
    public string Version { get; set; } = "0.0.0";
    public string Runtime { get; set; } = string.Empty;
    public string Channel { get; set; } = "release";
}
