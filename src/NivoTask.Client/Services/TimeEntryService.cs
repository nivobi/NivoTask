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

    public async Task<TimeSummaryResponse?> GetSummaryAsync()
        => await _http.GetFromJsonAsync<TimeSummaryResponse>("api/time-entries/summary");

    public async Task<List<DailyTotalResponse>> GetDailyAsync(int days = 7)
        => await _http.GetFromJsonAsync<List<DailyTotalResponse>>($"api/time-entries/daily?days={days}") ?? [];

    public async Task<List<TopTaskResponse>> GetTopTasksAsync(int days = 7, int take = 5)
        => await _http.GetFromJsonAsync<List<TopTaskResponse>>($"api/time-entries/top-tasks?days={days}&take={take}") ?? [];

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

    public async Task UpdateTimeEntryAsync(int entryId, UpdateTimeEntryRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/time-entries/{entryId}", request);
        response.EnsureSuccessStatusCode();
    }

    // ---------- Board-scoped ----------

    public async Task<HttpResponseMessage> StartBoardTimerAsync(int boardId, StartBoardTimerRequest request)
        => await _http.PostAsJsonAsync($"api/boards/{boardId}/timer/start", request);

    public async Task<TimeEntryResponse?> StopBoardTimerAsync(int boardId)
    {
        var response = await _http.PostAsync($"api/boards/{boardId}/timer/stop", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TimeEntryResponse>();
    }

    public async Task<TimeEntryResponse?> CreateBoardManualEntryAsync(int boardId, CreateBoardTimeEntryRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/boards/{boardId}/time-entries", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TimeEntryResponse>();
    }

    public async Task<List<TimeEntryResponse>> GetBoardTimeEntriesAsync(int boardId, int take = 20, int skip = 0)
        => await _http.GetFromJsonAsync<List<TimeEntryResponse>>($"api/boards/{boardId}/time-entries?take={take}&skip={skip}") ?? [];

    public async Task<BoardTimeSummaryResponse?> GetBoardTimeSummaryAsync(int boardId)
        => await _http.GetFromJsonAsync<BoardTimeSummaryResponse>($"api/boards/{boardId}/time-summary");
}
