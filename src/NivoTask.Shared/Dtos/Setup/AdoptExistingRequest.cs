using System.ComponentModel.DataAnnotations;

namespace NivoTask.Shared.Dtos.Setup;

public class AdoptExistingRequest
{
    [Required]
    public string Server { get; set; } = "localhost";

    public int Port { get; set; } = 3306;

    [Required]
    public string Database { get; set; } = "nivotask";

    [Required]
    public string DbUsername { get; set; } = string.Empty;

    [Required]
    public string DbPassword { get; set; } = string.Empty;
}
