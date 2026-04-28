using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.Setup;

public class CompleteSetupRequest
{
    // Database
    [Required]
    public string Server { get; set; } = "localhost";

    public int Port { get; set; } = 3306;

    [Required]
    public string Database { get; set; } = "nivotask";

    [Required]
    public string DbUsername { get; set; } = string.Empty;

    [Required]
    public string DbPassword { get; set; } = string.Empty;

    // Admin account
    [Required]
    [EmailAddress]
    public string AdminEmail { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string AdminPassword { get; set; } = string.Empty;

    // Optional first board
    public string? BoardName { get; set; }
}
