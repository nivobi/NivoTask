using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;
using NivoTask.Shared.Dtos.TimeEntries;

namespace NivoTask.Api.Tests.TimeEntries;

public class TimerStartStopTests : AuthenticatedTestBase
{
    public TimerStartStopTests(TestWebApplicationFactory factory) : base(factory) { }

    private async Task<(HttpClient client, int taskId)> SetupTaskAsync()
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
            new CreateBoardRequest { Name = "Timer Test Board" });
        var board = await boardRes.Content.ReadFromJsonAsync<BoardResponse>();
        var columnId = board!.Columns.First(c => !c.IsDone).Id;
        var taskRes = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "Timer Test Task" });
        var task = await taskRes.Content.ReadFromJsonAsync<TaskResponse>();
        return (client, task!.Id);
    }

    [Fact]
    public async Task StartTimer_ValidTask_Returns201WithActiveEntry()
    {
        var (client, taskId) = await SetupTaskAsync();

        var response = await client.PostAsync($"/api/tasks/{taskId}/timer/start", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<TimeEntryResponse>();
        Assert.NotNull(entry);
        Assert.True(entry.IsRunning);
        Assert.False(entry.IsManual);
        Assert.Equal(taskId, entry.TaskId);
        Assert.Equal(0, entry.DurationSeconds);
        Assert.NotNull(entry.StartTime);
        Assert.Null(entry.EndTime);
    }

    [Fact]
    public async Task StopTimer_RunningTimer_Returns200WithDuration()
    {
        var (client, taskId) = await SetupTaskAsync();
        await client.PostAsync($"/api/tasks/{taskId}/timer/start", null);

        var response = await client.PostAsync($"/api/tasks/{taskId}/timer/stop", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<TimeEntryResponse>();
        Assert.NotNull(entry);
        Assert.False(entry.IsRunning);
        Assert.True(entry.DurationSeconds >= 0);
        Assert.NotNull(entry.EndTime);
        Assert.NotNull(entry.StartTime);
    }

    [Fact]
    public async Task StopTimer_NoActiveTimer_Returns404()
    {
        var (client, taskId) = await SetupTaskAsync();

        var response = await client.PostAsync($"/api/tasks/{taskId}/timer/stop", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetActiveTimer_TimerRunning_Returns200()
    {
        var (client, taskId) = await SetupTaskAsync();
        await client.PostAsync($"/api/tasks/{taskId}/timer/start", null);

        var response = await client.GetAsync("/api/timer/active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var active = await response.Content.ReadFromJsonAsync<ActiveTimerResponse>();
        Assert.NotNull(active);
        Assert.Equal(taskId, active.TaskId);
        Assert.Equal("Timer Test Task", active.TaskTitle);
        Assert.True(active.ElapsedSeconds >= 0);
    }

    [Fact]
    public async Task GetActiveTimer_NoTimer_Returns204()
    {
        var (client, _) = await SetupTaskAsync();

        var response = await client.GetAsync("/api/timer/active");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task StartTimer_NonExistentTask_Returns404()
    {
        var client = await CreateAuthenticatedClient();

        var response = await client.PostAsync("/api/tasks/99999/timer/start", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
