using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;
using NivoTask.Shared.Dtos.TimeEntries;

namespace NivoTask.Api.Tests.TimeEntries;

public class BoardTimerTests : AuthenticatedTestBase
{
    public BoardTimerTests(TestWebApplicationFactory factory) : base(factory) { }

    private async Task<(HttpClient client, int boardId, int taskId)> SetupBoardWithTaskAsync()
    {
        var client = await CreateAuthenticatedClient();
        await StopAnyActiveTimerAsync(client);

        var boardRes = await client.PostAsJsonAsync("/api/boards",
            new CreateBoardRequest { Name = "Board Timer Test" });
        var board = await boardRes.Content.ReadFromJsonAsync<BoardResponse>();
        var columnId = board!.Columns.First(c => !c.IsDone).Id;
        var taskRes = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "Some task" });
        var task = await taskRes.Content.ReadFromJsonAsync<TaskResponse>();
        return (client, board.Id, task!.Id);
    }

    private static async Task StopAnyActiveTimerAsync(HttpClient client)
    {
        var activeRes = await client.GetAsync("/api/timer/active");
        if (activeRes.StatusCode != HttpStatusCode.OK) return;
        var active = await activeRes.Content.ReadFromJsonAsync<ActiveTimerResponse>();
        if (active is null) return;
        if (active.TaskId.HasValue)
            await client.PostAsync($"/api/tasks/{active.TaskId.Value}/timer/stop", null);
        else
            await client.PostAsync($"/api/boards/{active.BoardId}/timer/stop", null);
    }

    [Fact]
    public async Task StartBoardTimer_FreeEntry_Returns201WithNullTask()
    {
        var (client, boardId, _) = await SetupBoardWithTaskAsync();

        var res = await client.PostAsJsonAsync($"/api/boards/{boardId}/timer/start",
            new StartBoardTimerRequest { TaskId = null, Notes = "client X" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var entry = await res.Content.ReadFromJsonAsync<TimeEntryResponse>();
        Assert.NotNull(entry);
        Assert.True(entry.IsRunning);
        Assert.Null(entry.TaskId);
        Assert.Equal(boardId, entry.BoardId);
        Assert.Equal("client X", entry.Notes);
    }

    [Fact]
    public async Task StartBoardTimer_WithTask_LinksTaskAndBoard()
    {
        var (client, boardId, taskId) = await SetupBoardWithTaskAsync();

        var res = await client.PostAsJsonAsync($"/api/boards/{boardId}/timer/start",
            new StartBoardTimerRequest { TaskId = taskId });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var entry = await res.Content.ReadFromJsonAsync<TimeEntryResponse>();
        Assert.NotNull(entry);
        Assert.Equal(taskId, entry.TaskId);
        Assert.Equal(boardId, entry.BoardId);
    }

    [Fact]
    public async Task StartBoardTimer_TaskFromOtherBoard_Returns400()
    {
        var (client, boardA, _) = await SetupBoardWithTaskAsync();

        // Make a second board with its own task
        var bRes = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest { Name = "Board B" });
        var boardB = await bRes.Content.ReadFromJsonAsync<BoardResponse>();
        var colB = boardB!.Columns.First(c => !c.IsDone).Id;
        var tRes = await client.PostAsJsonAsync($"/api/boards/{boardB.Id}/columns/{colB}/tasks",
            new CreateTaskRequest { Title = "Task on B" });
        var taskB = await tRes.Content.ReadFromJsonAsync<TaskResponse>();

        var res = await client.PostAsJsonAsync($"/api/boards/{boardA}/timer/start",
            new StartBoardTimerRequest { TaskId = taskB!.Id });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task StartBoardTimer_WhenAnotherActive_Returns409()
    {
        var (client, boardA, _) = await SetupBoardWithTaskAsync();
        await client.PostAsJsonAsync($"/api/boards/{boardA}/timer/start", new StartBoardTimerRequest());

        var bRes = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest { Name = "Board C" });
        var boardB = await bRes.Content.ReadFromJsonAsync<BoardResponse>();

        var res = await client.PostAsJsonAsync($"/api/boards/{boardB!.Id}/timer/start",
            new StartBoardTimerRequest());

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var conflict = await res.Content.ReadFromJsonAsync<ActiveTimerResponse>();
        Assert.NotNull(conflict);
        Assert.Equal(boardA, conflict.BoardId);
    }

    [Fact]
    public async Task StopBoardTimer_RunningFreeEntry_Returns200()
    {
        var (client, boardId, _) = await SetupBoardWithTaskAsync();
        await client.PostAsJsonAsync($"/api/boards/{boardId}/timer/start",
            new StartBoardTimerRequest { Notes = "free" });

        var res = await client.PostAsync($"/api/boards/{boardId}/timer/stop", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var entry = await res.Content.ReadFromJsonAsync<TimeEntryResponse>();
        Assert.NotNull(entry);
        Assert.False(entry.IsRunning);
        Assert.Null(entry.TaskId);
        Assert.Equal(boardId, entry.BoardId);
    }

    [Fact]
    public async Task GetActiveTimer_FreeEntry_ReturnsBoardNameAndNullTask()
    {
        var (client, boardId, _) = await SetupBoardWithTaskAsync();
        await client.PostAsJsonAsync($"/api/boards/{boardId}/timer/start",
            new StartBoardTimerRequest { Notes = "X" });

        var res = await client.GetAsync("/api/timer/active");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var active = await res.Content.ReadFromJsonAsync<ActiveTimerResponse>();
        Assert.NotNull(active);
        Assert.Null(active.TaskId);
        Assert.Equal(boardId, active.BoardId);
        Assert.False(string.IsNullOrEmpty(active.BoardName));
    }

    [Fact]
    public async Task CreateBoardManualEntry_FreeEntry_PersistsWithNullTask()
    {
        var (client, boardId, _) = await SetupBoardWithTaskAsync();

        var res = await client.PostAsJsonAsync($"/api/boards/{boardId}/time-entries",
            new CreateBoardTimeEntryRequest { TaskId = null, DurationMinutes = 30, Notes = "design" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var entry = await res.Content.ReadFromJsonAsync<TimeEntryResponse>();
        Assert.NotNull(entry);
        Assert.True(entry.IsManual);
        Assert.Null(entry.TaskId);
        Assert.Equal(boardId, entry.BoardId);
        Assert.Equal(1800, entry.DurationSeconds);
    }

    [Fact]
    public async Task GetBoardTimeEntries_ReturnsBothFreeAndTaskEntries()
    {
        var (client, boardId, taskId) = await SetupBoardWithTaskAsync();
        await client.PostAsJsonAsync($"/api/boards/{boardId}/time-entries",
            new CreateBoardTimeEntryRequest { TaskId = null, DurationMinutes = 10, Notes = "free-1" });
        await client.PostAsJsonAsync($"/api/boards/{boardId}/time-entries",
            new CreateBoardTimeEntryRequest { TaskId = taskId, DurationMinutes = 20, Notes = "task-1" });

        var res = await client.GetAsync($"/api/boards/{boardId}/time-entries?take=20");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var entries = await res.Content.ReadFromJsonAsync<List<TimeEntryResponse>>();
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.TaskId == null);
        Assert.Contains(entries, e => e.TaskId == taskId);
    }
}
