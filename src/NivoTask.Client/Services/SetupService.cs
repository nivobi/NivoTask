using System.Net.Http.Json;
using NivoTask.Shared.Dtos.Setup;

namespace NivoTask.Client.Services;

public class SetupService
{
    private readonly HttpClient _http;
    private bool? _cachedStatus;

    public SetupService(HttpClient http) => _http = http;

    public async Task<bool> IsSetupCompleteAsync()
    {
        if (_cachedStatus.HasValue) return _cachedStatus.Value;

        try
        {
            var response = await _http.GetFromJsonAsync<SetupStatusResponse>("api/setup/status");
            _cachedStatus = response?.IsSetupComplete ?? false;
        }
        catch
        {
            _cachedStatus = false;
        }

        return _cachedStatus.Value;
    }

    public async Task<TestConnectionResponse> TestConnectionAsync(TestConnectionRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/setup/test-connection", request);
        var result = await response.Content.ReadFromJsonAsync<TestConnectionResponse>();
        return result ?? new TestConnectionResponse { Success = false, Error = "Empty response from server." };
    }

    public async Task<(bool Success, string? Error)> CompleteSetupAsync(CompleteSetupRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/setup/complete", request);
        var result = await response.Content.ReadFromJsonAsync<CompleteSetupResult>();

        if (response.IsSuccessStatusCode && (result?.Success ?? false))
            return (true, null);

        return (false, result?.Error ?? "Setup failed. Please try again.");
    }

    public async Task<(bool Success, string? Error)> AdoptExistingAsync(AdoptExistingRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/setup/adopt-existing", request);
        var result = await response.Content.ReadFromJsonAsync<CompleteSetupResult>();

        if (response.IsSuccessStatusCode && (result?.Success ?? false))
            return (true, null);

        return (false, result?.Error ?? "Adoption failed. Please try again.");
    }

    public void InvalidateCache() => _cachedStatus = null;

    private record CompleteSetupResult(bool Success, string? Error, string? Message);
}
