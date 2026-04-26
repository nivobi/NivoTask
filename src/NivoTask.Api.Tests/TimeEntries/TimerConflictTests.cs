using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;
using NivoTask.Shared.Dtos.TimeEntries;

namespace NivoTask.Api.Tests.TimeEntries;

public class TimerConflictTests : AuthenticatedTestBase
{
    public TimerConflictTests(TestWebApplicationFactory factory) : base(factory) { }

    private async Task<(HttpClient client, int taskId1, int taskId2)> SetupTwoTasksAsync()
    {
        var client = await CreateAuthenticatedClient();

        // Stop any pre-existing active timer from other tests (shared DB)
        var activeRes = await client.GetAsync("/api/timer/active");
        if (activeRes.StatusCode == HttpStatusCode.OK)
        {
            var active = await activeRes.Content.ReadFromJsonAsync<ActiveTimerResponse>();
            await client.PostAsync($"/api/tasks/{active!.TaskId}/timer/stop", null);
        }

        var boardRes = await client.PostAsJsonAsync("/api/boards",
            new CreateBoardRequest { Name = "Conflict Test Board" });
        var board = await boardRes.Content.ReadFromJsonAsync<BoardResponse>();
        var columnId = board!.Columns.First(c => !c.IsDone).Id;
        var task1Res = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "Task A" });
        var task1 = await task1Res.Content.ReadFromJsonAsync<TaskResponse>();
        var task2Res = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "Task B" });
        var task2 = await task2Res.Content.ReadFromJsonAsync<TaskResponse>();
        return (client, task1!.Id, task2!.Id);
    }

    [Fact]
    public async Task StartTimer_WhenTimerAlreadyActive_Returns409WithActiveInfo()
    {
        var (client, taskId1, taskId2) = await SetupTwoTasksAsync();
        await client.PostAsync($"/api/tasks/{taskId1}/timer/start", null);

        var response = await client.PostAsync($"/api/tasks/{taskId2}/timer/start", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var active = await response.Content.ReadFromJsonAsync<ActiveTimerResponse>();
        Assert.NotNull(active);
        Assert.Equal(taskId1, active.TaskId);
        Assert.Equal("Task A", active.TaskTitle);
        Assert.True(active.ElapsedSeconds >= 0);
    }

    [Fact]
    public async Task StartTimer_AfterStoppingPrevious_Returns201()
    {
        var (client, taskId1, taskId2) = await SetupTwoTasksAsync();
        await client.PostAsync($"/api/tasks/{taskId1}/timer/start", null);
        await client.PostAsync($"/api/tasks/{taskId1}/timer/stop", null);

        var response = await client.PostAsync($"/api/tasks/{taskId2}/timer/start", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task StopTimer_ThenStartOnSameTask_Returns201()
    {
        var (client, taskId1, _) = await SetupTwoTasksAsync();
        await client.PostAsync($"/api/tasks/{taskId1}/timer/start", null);
        await client.PostAsync($"/api/tasks/{taskId1}/timer/stop", null);

        var response = await client.PostAsync($"/api/tasks/{taskId1}/timer/start", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
