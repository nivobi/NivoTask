using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;
using NivoTask.Shared.Dtos.TimeEntries;

namespace NivoTask.Api.Tests.TimeEntries;

public class TimeRollupTests : AuthenticatedTestBase
{
    public TimeRollupTests(TestWebApplicationFactory factory) : base(factory) { }

    private async Task<(HttpClient client, int headTaskId, int subTaskId)> SetupHeadAndSubTaskAsync()
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
            new CreateBoardRequest { Name = "Rollup Test Board" });
        var board = await boardRes.Content.ReadFromJsonAsync<BoardResponse>();
        var columnId = board!.Columns.First(c => !c.IsDone).Id;
        var headRes = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "Head Task" });
        var head = await headRes.Content.ReadFromJsonAsync<TaskResponse>();
        var subRes = await client.PostAsJsonAsync(
            $"/api/tasks/{head!.Id}/subtasks",
            new CreateTaskRequest { Title = "Sub Task" });
        var sub = await subRes.Content.ReadFromJsonAsync<TaskResponse>();
        return (client, head.Id, sub!.Id);
    }

    [Fact]
    public async Task GetTask_NoEntries_TotalTimeSecondsIsZero()
    {
        var (client, headTaskId, _) = await SetupHeadAndSubTaskAsync();

        var response = await client.GetAsync($"/api/tasks/{headTaskId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal(0, detail.TotalTimeSeconds);
    }

    [Fact]
    public async Task GetTask_ManualEntryOnSubTask_RollsUpToHeadTask()
    {
        var (client, headTaskId, subTaskId) = await SetupHeadAndSubTaskAsync();

        // Add 45-min manual entry on sub-task
        await client.PostAsJsonAsync($"/api/tasks/{subTaskId}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 45 });

        var response = await client.GetAsync($"/api/tasks/{headTaskId}");

        var detail = await response.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.Equal(2700, detail!.TotalTimeSeconds); // 45 * 60
    }

    [Fact]
    public async Task GetTask_MultipleSubTaskEntries_SumsCorrectly()
    {
        var (client, headTaskId, subTaskId) = await SetupHeadAndSubTaskAsync();

        // Create a second sub-task
        var sub2Res = await client.PostAsJsonAsync(
            $"/api/tasks/{headTaskId}/subtasks",
            new CreateTaskRequest { Title = "Sub Task 2" });
        var sub2 = await sub2Res.Content.ReadFromJsonAsync<TaskResponse>();

        // Add 30-min manual entry on each sub-task
        await client.PostAsJsonAsync($"/api/tasks/{subTaskId}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 30 });
        await client.PostAsJsonAsync($"/api/tasks/{sub2!.Id}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 30 });

        var response = await client.GetAsync($"/api/tasks/{headTaskId}");

        var detail = await response.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.Equal(3600, detail!.TotalTimeSeconds); // 30*60 + 30*60
    }

    [Fact]
    public async Task GetTask_RunningTimerExcludedFromRollup()
    {
        var (client, headTaskId, subTaskId) = await SetupHeadAndSubTaskAsync();

        // Add 30-min manual entry (completed, counts toward rollup)
        await client.PostAsJsonAsync($"/api/tasks/{subTaskId}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 30 });

        // Start a timer on sub-task (running, should NOT count per D-05)
        await client.PostAsync($"/api/tasks/{subTaskId}/timer/start", null);

        var response = await client.GetAsync($"/api/tasks/{headTaskId}");

        var detail = await response.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.Equal(1800, detail!.TotalTimeSeconds); // Only the manual entry

        // Clean up: stop the timer
        await client.PostAsync($"/api/tasks/{subTaskId}/timer/stop", null);
    }

    [Fact]
    public async Task GetTask_DirectEntryOnHeadTask_IncludedInRollup()
    {
        var (client, headTaskId, _) = await SetupHeadAndSubTaskAsync();

        // Add 20-min manual entry directly on head task
        await client.PostAsJsonAsync($"/api/tasks/{headTaskId}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 20 });

        var response = await client.GetAsync($"/api/tasks/{headTaskId}");

        var detail = await response.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.Equal(1200, detail!.TotalTimeSeconds); // 20 * 60
    }

    [Fact]
    public async Task TimeEntryResponse_ContainsDurationSeconds_AsInteger()
    {
        var (client, _, subTaskId) = await SetupHeadAndSubTaskAsync();

        // Create 90-minute manual entry
        await client.PostAsJsonAsync($"/api/tasks/{subTaskId}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 90 });

        var response = await client.GetAsync($"/api/tasks/{subTaskId}/time-entries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<List<TimeEntryResponse>>();
        Assert.NotNull(entries);
        var entry = entries.First(e => e.DurationSeconds == 5400); // 90 * 60
        Assert.Equal(5400, entry.DurationSeconds);
    }
}
