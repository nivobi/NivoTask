using System.Net.Http.Json;
using NivoTask.Shared.Dtos.TimeEntries;

namespace NivoTask.Client.Services;

public class TimeEntryService
{
    private readonly HttpClient _http;

    public TimeEntryService(IHttpClientFactory factory)
        => _http = factory.CreateClient("Auth");

    public async Task<HttpResponseMessage> StartTimerAsync(int taskId)
        => await _http.PostAsync($"api/tasks/{taskId}/timer/start", null);

    public async Task<TimeEntryResponse?> StopTimerAsync(int taskId)
    {
        var response = await _http.PostAsync($"api/tasks/{taskId}/timer/stop", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TimeEntryResponse>();
    }

    public async Task<ActiveTimerResponse?> GetActiveTimerAsync()
    {
        var response = await _http.GetAsync("api/timer/active");
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ActiveTimerResponse>();
    }

    public async Task<List<TimeEntryResponse>> GetTimeEntriesAsync(int taskId)
        => await _http.GetFromJsonAsync<List<TimeEntryResponse>>($"api/tasks/{taskId}/time-entries") ?? [];

    public async Task<TimeEntryResponse?> CreateManualEntryAsync(int taskId, CreateTimeEntryRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/tasks/{taskId}/time-entries", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TimeEntryResponse>();
    }

    public async Task DeleteTimeEntryAsync(int entryId)
    {
        var response = await _http.DeleteAsync($"api/time-entries/{entryId}");
        response.EnsureSuccessStatusCode();
    }
}
