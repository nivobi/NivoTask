using System.Net.Http.Json;
using NivoTask.Client.Models;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;

namespace NivoTask.Client.Services;

public class BoardService
{
    private readonly HttpClient _http;

    public BoardService(IHttpClientFactory factory)
        => _http = factory.CreateClient("Auth");

    public async Task<List<BoardSummaryResponse>> GetBoardsAsync()
        => await _http.GetFromJsonAsync<List<BoardSummaryResponse>>("api/boards") ?? [];

    public async Task<BoardResponse?> GetBoardAsync(int boardId)
        => await _http.GetFromJsonAsync<BoardResponse>($"api/boards/{boardId}");

    public async Task<List<BoardTaskItem>> GetBoardTasksAsync(int boardId)
    {
        var dtos = await _http.GetFromJsonAsync<List<BoardTaskResponse>>($"api/boards/{boardId}/tasks") ?? [];
        return dtos.Select(d => new BoardTaskItem
        {
            Id = d.Id,
            Title = d.Title,
            SortOrder = d.SortOrder,
            ColumnIdentifier = d.ColumnId.ToString(),
            SubTaskCount = d.SubTaskCount,
            CompletedSubTaskCount = d.CompletedSubTaskCount,
            TotalTimeSeconds = d.TotalTimeSeconds
        }).ToList();
    }

    public async Task<BoardResponse?> CreateBoardAsync(CreateBoardRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/boards", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BoardResponse>();
    }

    public async Task UpdateBoardAsync(int boardId, UpdateBoardRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/boards/{boardId}", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteBoardAsync(int boardId)
    {
        var response = await _http.DeleteAsync($"api/boards/{boardId}");
        response.EnsureSuccessStatusCode();
    }
}
