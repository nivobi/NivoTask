using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NivoTask.Api.Data;
using NivoTask.Api.Models;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;

namespace NivoTask.Api.Tests.TimeEntries;

public class ExportDateRangeTests : AuthenticatedTestBase
{
    public ExportDateRangeTests(TestWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Export_WithFromAndTo_FiltersEntriesToRange()
    {
        var client = await CreateAuthenticatedClient();

        var boardRes = await client.PostAsJsonAsync("/api/boards",
            new CreateBoardRequest { Name = "Export Range Board" });
        var board = await boardRes.Content.ReadFromJsonAsync<BoardResponse>();
        var columnId = board!.Columns.First(c => !c.IsDone).Id;
        var taskRes = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{columnId}/tasks",
            new CreateTaskRequest { Title = "Export Task" });
        var task = await taskRes.Content.ReadFromJsonAsync<TaskResponse>();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = db.Users.Where(u => u.Email == "admin@nivotask.local").Select(u => u.Id).Single();

            // Seed three entries: in-range, before-range, after-range
            db.TimeEntries.Add(new TimeEntry
            {
                EndTime = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc),
                DurationSeconds = 1800,
                Notes = "in-range",
                BoardId = board.Id,
                TaskId = task!.Id,
                UserId = userId
            });
            db.TimeEntries.Add(new TimeEntry
            {
                EndTime = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
                DurationSeconds = 1800,
                Notes = "before-range",
                BoardId = board.Id,
                TaskId = task.Id,
                UserId = userId
            });
            db.TimeEntries.Add(new TimeEntry
            {
                EndTime = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Utc),
                DurationSeconds = 1800,
                Notes = "after-range",
                BoardId = board.Id,
                TaskId = task.Id,
                UserId = userId
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/time-entries/export?from=2026-04-01&to=2026-04-30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("in-range", csv);
        Assert.DoesNotContain("before-range", csv);
        Assert.DoesNotContain("after-range", csv);
    }

    [Fact]
    public async Task Export_DaysParam_StillWorksForBackwardCompat()
    {
        var client = await CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/time-entries/export?days=30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("﻿Date,Board,Task,DurationMinutes,Notes,IsManual", csv);
    }

    [Fact]
    public async Task Export_FromAfterTo_SwapsAndStillWorks()
    {
        var client = await CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/time-entries/export?from=2026-04-30&to=2026-04-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
