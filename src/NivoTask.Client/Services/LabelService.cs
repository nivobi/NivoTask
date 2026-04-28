using System.Net.Http.Json;
using NivoTask.Shared.Dtos.Labels;

namespace NivoTask.Client.Services;

public class LabelService
{
    private readonly HttpClient _http;

    public LabelService(HttpClient http) => _http = http;

    public async Task<List<LabelResponse>> GetLabelsAsync(int boardId)
        => await _http.GetFromJsonAsync<List<LabelResponse>>($"api/boards/{boardId}/labels") ?? [];

    public async Task<LabelResponse?> CreateLabelAsync(int boardId, CreateLabelRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/boards/{boardId}/labels", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LabelResponse>();
    }

    public async Task UpdateLabelAsync(int boardId, int labelId, UpdateLabelRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/boards/{boardId}/labels/{labelId}", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteLabelAsync(int boardId, int labelId)
    {
        var response = await _http.DeleteAsync($"api/boards/{boardId}/labels/{labelId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task SetTaskLabelsAsync(int taskId, List<int> labelIds)
    {
        var response = await _http.PutAsJsonAsync($"api/tasks/{taskId}/labels", new SetTaskLabelsRequest { LabelIds = labelIds });
        response.EnsureSuccessStatusCode();
    }
}
