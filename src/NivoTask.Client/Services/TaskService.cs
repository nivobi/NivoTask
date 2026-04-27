using System.Net.Http.Json;
using NivoTask.Shared.Dtos.Tasks;

namespace NivoTask.Client.Services;

public class TaskService
{
    private readonly HttpClient _http;

    public TaskService(IHttpClientFactory factory)
        => _http = factory.CreateClient("Auth");

    public async Task<TaskResponse?> CreateTaskAsync(int boardId, int columnId, CreateTaskRequest request)
    {
        var response = await _http.PostAsJsonAsync(
            $"api/boards/{boardId}/columns/{columnId}/tasks", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TaskResponse>();
    }

    public async Task MoveTaskAsync(int taskId, MoveTaskRequest request)
    {
        var response = await _http.PatchAsJsonAsync($"api/tasks/{taskId}/move", request);
        response.EnsureSuccessStatusCode();
    }
}
