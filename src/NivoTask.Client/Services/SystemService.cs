using System.Net.Http.Json;
using NivoTask.Shared.Dtos.System;

namespace NivoTask.Client.Services;

public class SystemService
{
    private readonly HttpClient _http;

    public SystemService(IHttpClientFactory factory)
        => _http = factory.CreateClient("Auth");

    public async Task<VersionInfoResponse?> GetVersionAsync()
        => await _http.GetFromJsonAsync<VersionInfoResponse>("api/system/version");

    public async Task<UpdateCheckResponse?> CheckForUpdatesAsync(bool refresh = false)
        => await _http.GetFromJsonAsync<UpdateCheckResponse>($"api/system/update-check?refresh={(refresh ? "true" : "false")}");

    public async Task<UpdateStartResponse?> StartUpdateAsync()
    {
        var response = await _http.PostAsync("api/system/update", null);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UpdateStartResponse>();
    }
}
