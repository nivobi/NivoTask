using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using NivoTask.Client.Identity.Models;

namespace NivoTask.Client.Identity;

public class CookieAuthenticationStateProvider : AuthenticationStateProvider, IAccountManagement
{
    private readonly HttpClient _httpClient;
    private bool _authenticated;
    private readonly ClaimsPrincipal _unauthenticated = new(new ClaimsIdentity());

    public CookieAuthenticationStateProvider(IHttpClientFactory factory)
    {
        _httpClient = factory.CreateClient("Auth");
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        _authenticated = false;
        var user = _unauthenticated;

        try
        {
            var info = await _httpClient.GetFromJsonAsync<UserInfo>("manage/info");
            if (info is not null)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.Name, info.Email),
                    new(ClaimTypes.Email, info.Email)
                };
                user = new ClaimsPrincipal(new ClaimsIdentity(claims, nameof(CookieAuthenticationStateProvider)));
                _authenticated = true;
            }
        }
        catch
        {
            // Not authenticated -- return unauthenticated principal
        }

        return new AuthenticationState(user);
    }

    public async Task<FormResult> LoginAsync(string email, string password)
    {
        try
        {
            var result = await _httpClient.PostAsJsonAsync(
                "login?useCookies=true", new { email, password });

            if (result.IsSuccessStatusCode)
            {
                NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
                return new FormResult { Succeeded = true };
            }

            // D-10: Generic error message -- no user enumeration
            return new FormResult
            {
                Succeeded = false,
                ErrorList = ["Invalid username or password."]
            };
        }
        catch
        {
            return new FormResult
            {
                Succeeded = false,
                ErrorList = ["An error occurred. Please try again."]
            };
        }
    }

    public async Task LogoutAsync()
    {
        await _httpClient.PostAsJsonAsync("logout", new { });
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
