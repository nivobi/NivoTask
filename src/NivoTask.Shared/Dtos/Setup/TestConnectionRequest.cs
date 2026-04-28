using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.Setup;

public class TestConnectionRequest
{
    [Required]
    public string Server { get; set; } = "localhost";

    public int Port { get; set; } = 3306;

    [Required]
    public string Database { get; set; } = "nivotask";

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
