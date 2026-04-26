using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;

namespace NivoTask.Api.Tests.Tasks;

public class TaskCrudTests : AuthenticatedTestBase
{
    public TaskCrudTests(TestWebApplicationFactory factory) : base(factory) { }

    private async Task<(HttpClient client, BoardResponse board, int columnId)> SetupBoardAsync()
    {
        var client = await CreateAuthenticatedClient();
        var boardRes = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest { Name = "TaskTest Board" });
        var board = await boardRes.Content.ReadFromJsonAsync<BoardResponse>();
        var columnId = board!.Columns.First(c => !c.IsDone).Id;
        return (client, board, columnId);
    }

    [Fact]
    public async Task CreateTask_ValidRequest_Returns201()
    {
        var (client, board, columnId) = await SetupBoardAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "My Task", Description = "Task notes" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(task);
        Assert.Equal("My Task", task.Title);
        Assert.Equal("Task notes", task.Description);
        Assert.Equal(columnId, task.ColumnId);
        Assert.Null(task.ParentTaskId);
    }

    [Fact]
    public async Task GetTask_ById_ReturnsTaskDetail()
    {
        var (client, board, columnId) = await SetupBoardAsync();
        var createRes = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "Detail Task" });
        var created = await createRes.Content.ReadFromJsonAsync<TaskResponse>();

        var response = await client.GetAsync($"/api/tasks/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal("Detail Task", detail.Title);
        Assert.Empty(detail.SubTasks);
    }

    [Fact]
    public async Task UpdateTask_ValidRequest_Returns204()
    {
        var (client, board, columnId) = await SetupBoardAsync();
        var createRes = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "Original Title" });
        var created = await createRes.Content.ReadFromJsonAsync<TaskResponse>();

        var updateRes = await client.PutAsJsonAsync(
            $"/api/tasks/{created!.Id}",
            new UpdateTaskRequest { Title = "Updated Title", Description = "Updated notes" });

        Assert.Equal(HttpStatusCode.NoContent, updateRes.StatusCode);

        var getRes = await client.GetAsync($"/api/tasks/{created.Id}");
        var detail = await getRes.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.Equal("Updated Title", detail!.Title);
        Assert.Equal("Updated notes", detail.Description);
    }

    [Fact]
    public async Task DeleteTask_ExistingTask_Returns204()
    {
        var (client, board, columnId) = await SetupBoardAsync();
        var createRes = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "Delete Me" });
        var created = await createRes.Content.ReadFromJsonAsync<TaskResponse>();

        var deleteRes = await client.DeleteAsync($"/api/tasks/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        var getRes = await client.GetAsync($"/api/tasks/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);
    }

    [Fact]
    public async Task CreateTask_EmptyTitle_Returns400()
    {
        var (client, board, columnId) = await SetupBoardAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTask_NonExistentId_Returns404()
    {
        var client = await CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/tasks/99999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateTask_WithDescription_StoresNotes()
    {
        var (client, board, columnId) = await SetupBoardAsync();
        var createRes = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "Notes Task", Description = "Detailed notes here" });
        var created = await createRes.Content.ReadFromJsonAsync<TaskResponse>();

        var getRes = await client.GetAsync($"/api/tasks/{created!.Id}");
        var detail = await getRes.Content.ReadFromJsonAsync<TaskDetailResponse>();

        Assert.Equal("Detailed notes here", detail!.Description);
    }
}
