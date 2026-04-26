using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;

namespace NivoTask.Api.Tests.Tasks;

public class SubTaskTests : AuthenticatedTestBase
{
    public SubTaskTests(TestWebApplicationFactory factory) : base(factory) { }

    private async Task<(HttpClient client, BoardResponse board, int columnId)> SetupBoardAsync()
    {
        var client = await CreateAuthenticatedClient();
        var boardRes = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest { Name = "SubTaskTest Board" });
        var board = await boardRes.Content.ReadFromJsonAsync<BoardResponse>();
        var columnId = board!.Columns.First(c => !c.IsDone).Id;
        return (client, board, columnId);
    }

    private async Task<TaskResponse> CreateHeadTaskAsync(HttpClient client, int boardId, int columnId, string title = "Head Task")
    {
        var res = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = title });
        return (await res.Content.ReadFromJsonAsync<TaskResponse>())!;
    }

    [Fact]
    public async Task CreateSubTask_ValidRequest_Returns201()
    {
        var (client, board, columnId) = await SetupBoardAsync();
        var headTask = await CreateHeadTaskAsync(client, board.Id, columnId);

        var response = await client.PostAsJsonAsync(
            $"/api/tasks/{headTask.Id}/subtasks",
            new CreateTaskRequest { Title = "Sub-task 1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var subTask = await response.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(subTask);
        Assert.Equal("Sub-task 1", subTask.Title);
        Assert.Equal(headTask.Id, subTask.ParentTaskId);
        Assert.Equal(headTask.ColumnId, subTask.ColumnId);
    }

    [Fact]
    public async Task CreateSubTask_InheritsParentColumn()
    {
        var (client, board, columnId) = await SetupBoardAsync();
        var headTask = await CreateHeadTaskAsync(client, board.Id, columnId);

        var subRes = await client.PostAsJsonAsync(
            $"/api/tasks/{headTask.Id}/subtasks",
            new CreateTaskRequest { Title = "Inherit Column Sub" });
        var subTask = await subRes.Content.ReadFromJsonAsync<TaskResponse>();

        // GET the sub-task detail to verify column inheritance
        var getRes = await client.GetAsync($"/api/tasks/{subTask!.Id}");
        var detail = await getRes.Content.ReadFromJsonAsync<TaskDetailResponse>();

        Assert.Equal(headTask.ColumnId, detail!.ColumnId);
    }

    [Fact]
    public async Task UpdateSubTask_ValidRequest_Returns204()
    {
        var (client, board, columnId) = await SetupBoardAsync();
        var headTask = await CreateHeadTaskAsync(client, board.Id, columnId);
        var subRes = await client.PostAsJsonAsync(
            $"/api/tasks/{headTask.Id}/subtasks",
            new CreateTaskRequest { Title = "Original Sub" });
        var subTask = await subRes.Content.ReadFromJsonAsync<TaskResponse>();

        var updateRes = await client.PutAsJsonAsync(
            $"/api/tasks/{subTask!.Id}",
            new UpdateTaskRequest { Title = "Updated Sub", Description = "Sub notes" });

        Assert.Equal(HttpStatusCode.NoContent, updateRes.StatusCode);

        var getRes = await client.GetAsync($"/api/tasks/{subTask.Id}");
        var detail = await getRes.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.Equal("Updated Sub", detail!.Title);
        Assert.Equal("Sub notes", detail.Description);
    }

    [Fact]
    public async Task DeleteSubTask_Returns204_ParentUnaffected()
    {
        var (client, board, columnId) = await SetupBoardAsync();
        var headTask = await CreateHeadTaskAsync(client, board.Id, columnId);

        var sub1Res = await client.PostAsJsonAsync(
            $"/api/tasks/{headTask.Id}/subtasks",
            new CreateTaskRequest { Title = "Sub 1" });
        var sub1 = await sub1Res.Content.ReadFromJsonAsync<TaskResponse>();

        await client.PostAsJsonAsync(
            $"/api/tasks/{headTask.Id}/subtasks",
            new CreateTaskRequest { Title = "Sub 2" });

        var deleteRes = await client.DeleteAsync($"/api/tasks/{sub1!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        var getRes = await client.GetAsync($"/api/tasks/{headTask.Id}");
        var detail = await getRes.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.Single(detail!.SubTasks);
        Assert.Equal("Sub 2", detail.SubTasks[0].Title);
    }

    [Fact]
    public async Task CreateSubTask_OnSubTask_Returns404()
    {
        var (client, board, columnId) = await SetupBoardAsync();
        var headTask = await CreateHeadTaskAsync(client, board.Id, columnId);

        var subRes = await client.PostAsJsonAsync(
            $"/api/tasks/{headTask.Id}/subtasks",
            new CreateTaskRequest { Title = "Sub-task" });
        var subTask = await subRes.Content.ReadFromJsonAsync<TaskResponse>();

        // Attempt to create a sub-sub-task
        var response = await client.PostAsJsonAsync(
            $"/api/tasks/{subTask!.Id}/subtasks",
            new CreateTaskRequest { Title = "Sub-sub-task" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteHeadTask_CascadesSubTasks()
    {
        var (client, board, columnId) = await SetupBoardAsync();
        var headTask = await CreateHeadTaskAsync(client, board.Id, columnId);

        var sub1Res = await client.PostAsJsonAsync(
            $"/api/tasks/{headTask.Id}/subtasks",
            new CreateTaskRequest { Title = "Cascade Sub 1" });
        var sub1 = await sub1Res.Content.ReadFromJsonAsync<TaskResponse>();

        var sub2Res = await client.PostAsJsonAsync(
            $"/api/tasks/{headTask.Id}/subtasks",
            new CreateTaskRequest { Title = "Cascade Sub 2" });
        var sub2 = await sub2Res.Content.ReadFromJsonAsync<TaskResponse>();

        // Delete the head task
        var deleteRes = await client.DeleteAsync($"/api/tasks/{headTask.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        // Verify sub-tasks are also gone
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tasks/{sub1!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tasks/{sub2!.Id}")).StatusCode);
    }
}
