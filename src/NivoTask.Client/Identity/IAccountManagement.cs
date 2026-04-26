using NivoTask.Client.Identity.Models;

namespace NivoTask.Client.Identity;

public interface IAccountManagement
{
    Task<FormResult> LoginAsync(string email, string password);
    Task LogoutAsync();
}
