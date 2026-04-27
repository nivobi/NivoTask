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

    public async Task<TaskDetailResponse?> GetTaskDetailAsync(int taskId)
        => await _http.GetFromJsonAsync<TaskDetailResponse>($"api/tasks/{taskId}");

    public async Task<TaskResponse?> CreateSubTaskAsync(int parentTaskId, CreateTaskRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/tasks/{parentTaskId}/subtasks", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TaskResponse>();
    }

    public async Task UpdateTaskAsync(int taskId, UpdateTaskRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/tasks/{taskId}", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteTaskAsync(int taskId)
    {
        var response = await _http.DeleteAsync($"api/tasks/{taskId}");
        response.EnsureSuccessStatusCode();
    }
}
