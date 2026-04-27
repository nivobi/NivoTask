using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;

namespace NivoTask.Api.Tests.Boards;

public class GetBoardTasksTests : AuthenticatedTestBase
{
    public GetBoardTasksTests(TestWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetBoardTasks_ReturnsHeadTasksOnly()
    {
        var client = await CreateAuthenticatedClient();

        // Create board
        var boardResp = await client.PostAsJsonAsync("api/boards",
            new { Name = "TasksTest", Color = "#000", Icon = "folder" });
        var board = await boardResp.Content.ReadFromJsonAsync<BoardResponse>();

        // Create head task in first column
        var columnId = board!.Columns[0].Id;
        var taskResp = await client.PostAsJsonAsync(
            $"api/boards/{board.Id}/columns/{columnId}/tasks",
            new { Title = "Head Task 1" });
        var headTask = await taskResp.Content.ReadFromJsonAsync<TaskResponse>();

        // Create sub-task under head task
        await client.PostAsJsonAsync(
            $"api/tasks/{headTask!.Id}/subtasks",
            new { Title = "Sub Task 1" });

        // Fetch board tasks
        var response = await client.GetAsync($"api/boards/{board.Id}/tasks");
        response.EnsureSuccessStatusCode();
        var tasks = await response.Content.ReadFromJsonAsync<List<BoardTaskResponse>>();

        Assert.NotNull(tasks);
        Assert.Single(tasks); // Only head task, no sub-tasks
        Assert.Equal("Head Task 1", tasks[0].Title);
        Assert.Equal(1, tasks[0].SubTaskCount);
        Assert.Equal(columnId, tasks[0].ColumnId);
    }

    [Fact]
    public async Task GetBoardTasks_NonexistentBoard_Returns404()
    {
        var client = await CreateAuthenticatedClient();
        var response = await client.GetAsync("api/boards/99999/tasks");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBoardTasks_EmptyBoard_ReturnsEmptyList()
    {
        var client = await CreateAuthenticatedClient();
        var boardResp = await client.PostAsJsonAsync("api/boards",
            new { Name = "EmptyBoard", Color = "#000", Icon = "folder" });
        var board = await boardResp.Content.ReadFromJsonAsync<BoardResponse>();

        var response = await client.GetAsync($"api/boards/{board!.Id}/tasks");
        response.EnsureSuccessStatusCode();
        var tasks = await response.Content.ReadFromJsonAsync<List<BoardTaskResponse>>();

        Assert.NotNull(tasks);
        Assert.Empty(tasks);
    }
}
