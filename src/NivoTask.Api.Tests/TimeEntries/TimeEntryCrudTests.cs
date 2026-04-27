using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;
using NivoTask.Shared.Dtos.TimeEntries;

namespace NivoTask.Api.Tests.TimeEntries;

public class TimeEntryCrudTests : AuthenticatedTestBase
{
    public TimeEntryCrudTests(TestWebApplicationFactory factory) : base(factory) { }

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
            new CreateBoardRequest { Name = "CRUD Test Board" });
        var board = await boardRes.Content.ReadFromJsonAsync<BoardResponse>();
        var columnId = board!.Columns.First(c => !c.IsDone).Id;
        var taskRes = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "CRUD Test Task" });
        var task = await taskRes.Content.ReadFromJsonAsync<TaskResponse>();
        return (client, task!.Id);
    }

    [Fact]
    public async Task CreateManualEntry_ValidRequest_Returns201()
    {
        var (client, taskId) = await SetupTaskAsync();

        var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 30, Notes = "Manual work" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<TimeEntryResponse>();
        Assert.NotNull(entry);
        Assert.True(entry.IsManual);
        Assert.False(entry.IsRunning);
        Assert.Equal(1800, entry.DurationSeconds); // 30 * 60
        Assert.Equal("Manual work", entry.Notes);
        Assert.Null(entry.StartTime);
        Assert.NotNull(entry.EndTime); // Manual entries have EndTime set to avoid active timer index conflict
    }

    [Fact]
    public async Task CreateManualEntry_InvalidDuration_Returns400()
    {
        var (client, taskId) = await SetupTaskAsync();

        var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateManualEntry_ExceedsMax_Returns400()
    {
        var (client, taskId) = await SetupTaskAsync();

        var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 1441 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTimeEntries_ReturnsAllEntries()
    {
        var (client, taskId) = await SetupTaskAsync();
        await client.PostAsJsonAsync($"/api/tasks/{taskId}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 30 });
        await client.PostAsJsonAsync($"/api/tasks/{taskId}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 60 });

        var response = await client.GetAsync($"/api/tasks/{taskId}/time-entries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<List<TimeEntryResponse>>();
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.True(e.IsManual));
    }

    [Fact]
    public async Task GetTimeEntries_EmptyList_Returns200()
    {
        var (client, taskId) = await SetupTaskAsync();

        var response = await client.GetAsync($"/api/tasks/{taskId}/time-entries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<List<TimeEntryResponse>>();
        Assert.NotNull(entries);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task UpdateTimeEntry_ValidRequest_Returns204()
    {
        var (client, taskId) = await SetupTaskAsync();
        var createRes = await client.PostAsJsonAsync($"/api/tasks/{taskId}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 30, Notes = "Original" });
        var created = await createRes.Content.ReadFromJsonAsync<TimeEntryResponse>();

        var updateRes = await client.PutAsJsonAsync($"/api/time-entries/{created!.Id}",
            new UpdateTimeEntryRequest { DurationSeconds = 3600, Notes = "Updated" });

        Assert.Equal(HttpStatusCode.NoContent, updateRes.StatusCode);

        // Verify the update
        var listRes = await client.GetAsync($"/api/tasks/{taskId}/time-entries");
        var entries = await listRes.Content.ReadFromJsonAsync<List<TimeEntryResponse>>();
        var entry = entries!.First(e => e.Id == created.Id);
        Assert.Equal(3600, entry.DurationSeconds);
        Assert.Equal("Updated", entry.Notes);
    }

    [Fact]
    public async Task DeleteTimeEntry_ValidRequest_Returns204()
    {
        var (client, taskId) = await SetupTaskAsync();
        var createRes = await client.PostAsJsonAsync($"/api/tasks/{taskId}/time-entries",
            new CreateTimeEntryRequest { DurationMinutes = 30 });
        var created = await createRes.Content.ReadFromJsonAsync<TimeEntryResponse>();

        var deleteRes = await client.DeleteAsync($"/api/time-entries/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        // Verify deletion
        var listRes = await client.GetAsync($"/api/tasks/{taskId}/time-entries");
        var entries = await listRes.Content.ReadFromJsonAsync<List<TimeEntryResponse>>();
        Assert.DoesNotContain(entries!, e => e.Id == created.Id);
    }

    [Fact]
    public async Task UpdateTimeEntry_NonExistent_Returns404()
    {
        var client = await CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync("/api/time-entries/99999",
            new UpdateTimeEntryRequest { DurationSeconds = 3600 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
