using System.Net.Http.Json;
using NivoTask.Shared.Dtos.Columns;

namespace NivoTask.Client.Services;

public class ColumnService
{
    private readonly HttpClient _http;

    public ColumnService(IHttpClientFactory factory)
        => _http = factory.CreateClient("Auth");

    public async Task<ColumnResponse?> CreateColumnAsync(int boardId, CreateColumnRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/boards/{boardId}/columns", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ColumnResponse>();
    }

    public async Task UpdateColumnAsync(int boardId, int columnId, UpdateColumnRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/boards/{boardId}/columns/{columnId}", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteColumnAsync(int boardId, int columnId)
    {
        var response = await _http.DeleteAsync($"api/boards/{boardId}/columns/{columnId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task ReorderColumnsAsync(int boardId, ReorderColumnsRequest request)
    {
        var response = await _http.PatchAsJsonAsync($"api/boards/{boardId}/columns/reorder", request);
        response.EnsureSuccessStatusCode();
    }
}
