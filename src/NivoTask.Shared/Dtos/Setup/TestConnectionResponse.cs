namespace NivoTask.Shared.Dtos.Setup;

public class TestConnectionResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool MigrationsApplied { get; set; }
    public bool HasExistingUser { get; set; }
    public string? ExistingUserEmail { get; set; }
}
