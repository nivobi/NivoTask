using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NivoTask.Api.Data;
using NivoTask.Api.Models;
using NivoTask.Api.Services;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;

namespace NivoTask.Api.Tests.TimeEntries;

public class StaleTimerCleanupTests : AuthenticatedTestBase
{
    public StaleTimerCleanupTests(TestWebApplicationFactory factory) : base(factory) { }

    private async Task<(HttpClient client, string userId, int boardId, int taskId)> SetupAsync()
    {
        var client = await CreateAuthenticatedClient();

        // Stop any pre-existing active timer from other tests
        var activeRes = await client.GetAsync("/api/timer/active");
        if (activeRes.StatusCode == HttpStatusCode.OK)
        {
            var active = await activeRes.Content.ReadFromJsonAsync<NivoTask.Shared.Dtos.TimeEntries.ActiveTimerResponse>();
            await client.PostAsync($"/api/tasks/{active!.TaskId}/timer/stop", null);
        }

        var boardRes = await client.PostAsJsonAsync("/api/boards",
            new CreateBoardRequest { Name = "Stale Timer Board" });
        var board = await boardRes.Content.ReadFromJsonAsync<BoardResponse>();
        var columnId = board!.Columns.First(c => !c.IsDone).Id;
        var taskRes = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "Stale Timer Task" });
        var task = await taskRes.Content.ReadFromJsonAsync<TaskResponse>();

        // Resolve userId from seeded admin user
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = db.Users.Where(u => u.Email == "admin@nivotask.local").Select(u => u.Id).Single();

        return (client, userId, board.Id, task!.Id);
    }

    [Fact]
    public async Task SweepAsync_ClosesTimersOlderThan24Hours()
    {
        var (client, userId, boardId, taskId) = await SetupAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TimeEntries.Add(new TimeEntry
            {
                StartTime = DateTime.UtcNow.AddHours(-25),
                EndTime = null,
                DurationSeconds = 0,
                BoardId = boardId,
                TaskId = taskId,
                UserId = userId
            });
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var sweeper = scope.ServiceProvider.GetServices<IHostedService>()
                .OfType<StaleTimerCleanupService>()
                .Single();
            await sweeper.SweepAsync(CancellationToken.None);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db.TimeEntries
                .Where(te => te.UserId == userId && te.BoardId == boardId)
                .OrderByDescending(te => te.Id)
                .FirstAsync();
            Assert.NotNull(entry.EndTime);
            Assert.Equal(86400, entry.DurationSeconds);
        }
    }

    [Fact]
    public async Task SweepAsync_LeavesFreshTimersAlone()
    {
        var (client, userId, boardId, taskId) = await SetupAsync();

        int entryId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = new TimeEntry
            {
                StartTime = DateTime.UtcNow.AddMinutes(-30),
                EndTime = null,
                DurationSeconds = 0,
                BoardId = boardId,
                TaskId = taskId,
                UserId = userId
            };
            db.TimeEntries.Add(entry);
            await db.SaveChangesAsync();
            entryId = entry.Id;
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var sweeper = scope.ServiceProvider.GetServices<IHostedService>()
                .OfType<StaleTimerCleanupService>()
                .Single();
            await sweeper.SweepAsync(CancellationToken.None);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db.TimeEntries.FindAsync(entryId);
            Assert.NotNull(entry);
            Assert.Null(entry!.EndTime);
            Assert.Equal(0, entry.DurationSeconds);
        }
    }

    [Fact]
    public async Task GetActiveTimer_LazilyClosesStaleTimer()
    {
        var (client, userId, boardId, taskId) = await SetupAsync();

        int entryId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = new TimeEntry
            {
                StartTime = DateTime.UtcNow.AddHours(-30),
                EndTime = null,
                DurationSeconds = 0,
                BoardId = boardId,
                TaskId = taskId,
                UserId = userId
            };
            db.TimeEntries.Add(entry);
            await db.SaveChangesAsync();
            entryId = entry.Id;
        }

        var response = await client.GetAsync("/api/timer/active");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db.TimeEntries.FindAsync(entryId);
            Assert.NotNull(entry);
            Assert.NotNull(entry!.EndTime);
            Assert.Equal(86400, entry.DurationSeconds);
        }
    }
}
