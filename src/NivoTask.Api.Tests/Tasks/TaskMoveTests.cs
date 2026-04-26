using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;

namespace NivoTask.Api.Tests.Tasks;

public class TaskMoveTests : AuthenticatedTestBase
{
    public TaskMoveTests(TestWebApplicationFactory factory) : base(factory) { }

    private async Task<(HttpClient client, BoardResponse board, int todoColumnId, int inProgressColumnId)> SetupBoardAsync()
    {
        var client = await CreateAuthenticatedClient();
        var boardRes = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest { Name = "MoveTest Board" });
        var board = await boardRes.Content.ReadFromJsonAsync<BoardResponse>();
        var todoCol = board!.Columns.First(c => c.Name == "To Do").Id;
        var inProgressCol = board.Columns.First(c => c.Name == "In Progress").Id;
        return (client, board, todoCol, inProgressCol);
    }

    private async Task<TaskResponse> CreateHeadTaskAsync(HttpClient client, int boardId, int columnId, string title = "Move Task")
    {
        var res = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = title });
        return (await res.Content.ReadFromJsonAsync<TaskResponse>())!;
    }

    [Fact]
    public async Task MoveTask_ToDifferentColumn_Returns204()
    {
        var (client, board, todoCol, inProgressCol) = await SetupBoardAsync();
        var task = await CreateHeadTaskAsync(client, board.Id, todoCol);

        var moveRes = await client.PatchAsJsonAsync(
            $"/api/tasks/{task.Id}/move",
            new MoveTaskRequest { TargetColumnId = inProgressCol, NewSortOrder = 1000 });

        Assert.Equal(HttpStatusCode.NoContent, moveRes.StatusCode);

        var getRes = await client.GetAsync($"/api/tasks/{task.Id}");
        var detail = await getRes.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.Equal(inProgressCol, detail!.ColumnId);
    }

    [Fact]
    public async Task MoveTask_SubTasksFollowParent()
    {
        var (client, board, todoCol, inProgressCol) = await SetupBoardAsync();
        var headTask = await CreateHeadTaskAsync(client, board.Id, todoCol);

        var sub1Res = await client.PostAsJsonAsync(
            $"/api/tasks/{headTask.Id}/subtasks",
            new CreateTaskRequest { Title = "Follow Sub 1" });
        var sub1 = await sub1Res.Content.ReadFromJsonAsync<TaskResponse>();

        var sub2Res = await client.PostAsJsonAsync(
            $"/api/tasks/{headTask.Id}/subtasks",
            new CreateTaskRequest { Title = "Follow Sub 2" });
        var sub2 = await sub2Res.Content.ReadFromJsonAsync<TaskResponse>();

        // Move head task to In Progress
        await client.PatchAsJsonAsync(
            $"/api/tasks/{headTask.Id}/move",
            new MoveTaskRequest { TargetColumnId = inProgressCol, NewSortOrder = 1000 });

        // Verify sub-tasks followed
        var sub1Detail = await (await client.GetAsync($"/api/tasks/{sub1!.Id}")).Content.ReadFromJsonAsync<TaskDetailResponse>();
        var sub2Detail = await (await client.GetAsync($"/api/tasks/{sub2!.Id}")).Content.ReadFromJsonAsync<TaskDetailResponse>();

        Assert.Equal(inProgressCol, sub1Detail!.ColumnId);
        Assert.Equal(inProgressCol, sub2Detail!.ColumnId);
    }

    [Fact]
    public async Task MoveTask_ToColumnInDifferentBoard_ReturnsBadRequest()
    {
        var (client, board1, todoCol, _) = await SetupBoardAsync();
        var task = await CreateHeadTaskAsync(client, board1.Id, todoCol);

        // Create a second board
        var board2Res = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest { Name = "Other Board" });
        var board2 = await board2Res.Content.ReadFromJsonAsync<BoardResponse>();
        var otherColumnId = board2!.Columns.First().Id;

        var moveRes = await client.PatchAsJsonAsync(
            $"/api/tasks/{task.Id}/move",
            new MoveTaskRequest { TargetColumnId = otherColumnId, NewSortOrder = 1000 });

        Assert.Equal(HttpStatusCode.BadRequest, moveRes.StatusCode);
    }

    [Fact]
    public async Task MoveTask_SubTask_Returns404()
    {
        var (client, board, todoCol, inProgressCol) = await SetupBoardAsync();
        var headTask = await CreateHeadTaskAsync(client, board.Id, todoCol);

        var subRes = await client.PostAsJsonAsync(
            $"/api/tasks/{headTask.Id}/subtasks",
            new CreateTaskRequest { Title = "Unmovable Sub" });
        var subTask = await subRes.Content.ReadFromJsonAsync<TaskResponse>();

        var moveRes = await client.PatchAsJsonAsync(
            $"/api/tasks/{subTask!.Id}/move",
            new MoveTaskRequest { TargetColumnId = inProgressCol, NewSortOrder = 1000 });

        Assert.Equal(HttpStatusCode.NotFound, moveRes.StatusCode);
    }

    [Fact]
    public async Task ReorderTasks_ValidOrder_Returns204()
    {
        var (client, board, todoCol, _) = await SetupBoardAsync();
        var task1 = await CreateHeadTaskAsync(client, board.Id, todoCol, "Reorder 1");
        var task2 = await CreateHeadTaskAsync(client, board.Id, todoCol, "Reorder 2");
        var task3 = await CreateHeadTaskAsync(client, board.Id, todoCol, "Reorder 3");

        // Reverse the order
        var reorderRes = await client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{todoCol}/tasks/reorder",
            new ReorderTasksRequest { TaskIds = [task3.Id, task2.Id, task1.Id] });

        Assert.Equal(HttpStatusCode.NoContent, reorderRes.StatusCode);

        // Verify: task3 should now have the lowest SortOrder
        var t3Detail = await (await client.GetAsync($"/api/tasks/{task3.Id}")).Content.ReadFromJsonAsync<TaskDetailResponse>();
        var t2Detail = await (await client.GetAsync($"/api/tasks/{task2.Id}")).Content.ReadFromJsonAsync<TaskDetailResponse>();
        var t1Detail = await (await client.GetAsync($"/api/tasks/{task1.Id}")).Content.ReadFromJsonAsync<TaskDetailResponse>();

        Assert.True(t3Detail!.SortOrder < t2Detail!.SortOrder);
        Assert.True(t2Detail.SortOrder < t1Detail!.SortOrder);
    }

    [Fact]
    public async Task ReorderTasks_MissingTaskId_Returns400()
    {
        var (client, board, todoCol, _) = await SetupBoardAsync();
        var task1 = await CreateHeadTaskAsync(client, board.Id, todoCol, "Missing 1");
        var task2 = await CreateHeadTaskAsync(client, board.Id, todoCol, "Missing 2");
        await CreateHeadTaskAsync(client, board.Id, todoCol, "Missing 3");

        // Only send 2 of 3 task IDs
        var reorderRes = await client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{todoCol}/tasks/reorder",
            new ReorderTasksRequest { TaskIds = [task2.Id, task1.Id] });

        Assert.Equal(HttpStatusCode.BadRequest, reorderRes.StatusCode);
    }
}
