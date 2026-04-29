using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;
using NivoTask.Shared.Dtos.TimeEntries;

namespace NivoTask.Api.Tests.TimeEntries;

public class BoardTimeSummaryTests : AuthenticatedTestBase
{
    public BoardTimeSummaryTests(TestWebApplicationFactory factory) : base(factory) { }

    private async Task<(HttpClient client, int boardId, int taskId)> SetupAsync()
    {
        var client = await CreateAuthenticatedClient();

        var activeRes = await client.GetAsync("/api/timer/active");
        if (activeRes.StatusCode == HttpStatusCode.OK)
        {
            var active = await activeRes.Content.ReadFromJsonAsync<ActiveTimerResponse>();
            if (active is not null)
            {
                if (active.TaskId.HasValue)
                    await client.PostAsync($"/api/tasks/{active.TaskId.Value}/timer/stop", null);
                else
                    await client.PostAsync($"/api/boards/{active.BoardId}/timer/stop", null);
            }
        }

        var boardRes = await client.PostAsJsonAsync("/api/boards",
            new CreateBoardRequest { Name = "Summary Test Board" });
        var board = await boardRes.Content.ReadFromJsonAsync<BoardResponse>();
        var columnId = board!.Columns.First(c => !c.IsDone).Id;
        var taskRes = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "Summary Task" });
        var task = await taskRes.Content.ReadFromJsonAsync<TaskResponse>();
        return (client, board.Id, task!.Id);
    }

    [Fact]
    public async Task BoardSummary_IncludesFreeAndTaskEntries()
    {
        var (client, boardId, taskId) = await SetupAsync();

        await client.PostAsJsonAsync($"/api/boards/{boardId}/time-entries",
            new CreateBoardTimeEntryRequest { TaskId = null, DurationMinutes = 10 });
        await client.PostAsJsonAsync($"/api/boards/{boardId}/time-entries",
            new CreateBoardTimeEntryRequest { TaskId = taskId, DurationMinutes = 20 });

        var res = await client.GetAsync($"/api/boards/{boardId}/time-summary");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var summary = await res.Content.ReadFromJsonAsync<BoardTimeSummaryResponse>();
        Assert.NotNull(summary);
        Assert.Equal(boardId, summary.BoardId);
        Assert.Equal(30 * 60, summary.AllTimeSeconds);
        Assert.True(summary.TodaySeconds == 30 * 60);
        Assert.Equal(2, summary.TodayEntryCount);
    }

    [Fact]
    public async Task BoardSummary_ExcludesRunningTimer()
    {
        var (client, boardId, _) = await SetupAsync();

        await client.PostAsJsonAsync($"/api/boards/{boardId}/time-entries",
            new CreateBoardTimeEntryRequest { DurationMinutes = 5 });
        await client.PostAsJsonAsync($"/api/boards/{boardId}/timer/start",
            new StartBoardTimerRequest());

        var res = await client.GetAsync($"/api/boards/{boardId}/time-summary");
        var summary = await res.Content.ReadFromJsonAsync<BoardTimeSummaryResponse>();

        Assert.NotNull(summary);
        // Only the manual 5-minute entry counts; running timer DurationSeconds is 0
        Assert.Equal(5 * 60, summary.AllTimeSeconds);
    }

    [Fact]
    public async Task BoardSummary_OtherBoardEntriesNotCounted()
    {
        var (client, boardA, _) = await SetupAsync();
        var bRes = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest { Name = "Other" });
        var boardB = await bRes.Content.ReadFromJsonAsync<BoardResponse>();

        await client.PostAsJsonAsync($"/api/boards/{boardA}/time-entries",
            new CreateBoardTimeEntryRequest { DurationMinutes = 7 });
        await client.PostAsJsonAsync($"/api/boards/{boardB!.Id}/time-entries",
            new CreateBoardTimeEntryRequest { DurationMinutes = 13 });

        var resA = await client.GetAsync($"/api/boards/{boardA}/time-summary");
        var sumA = await resA.Content.ReadFromJsonAsync<BoardTimeSummaryResponse>();
        Assert.Equal(7 * 60, sumA!.AllTimeSeconds);

        var resB = await client.GetAsync($"/api/boards/{boardB.Id}/time-summary");
        var sumB = await resB.Content.ReadFromJsonAsync<BoardTimeSummaryResponse>();
        Assert.Equal(13 * 60, sumB!.AllTimeSeconds);
    }

    [Fact]
    public async Task TaskRollup_IgnoresFreeBoardEntries()
    {
        var (client, boardId, taskId) = await SetupAsync();

        await client.PostAsJsonAsync($"/api/boards/{boardId}/time-entries",
            new CreateBoardTimeEntryRequest { TaskId = null, DurationMinutes = 30 });
        await client.PostAsJsonAsync($"/api/boards/{boardId}/time-entries",
            new CreateBoardTimeEntryRequest { TaskId = taskId, DurationMinutes = 10 });

        // Task rollup should only include the 10-minute task-bound entry
        var taskDetail = await client.GetFromJsonAsync<TaskDetailResponse>($"/api/tasks/{taskId}");
        Assert.NotNull(taskDetail);
        Assert.Equal(10 * 60, taskDetail.TotalTimeSeconds);
    }
}
