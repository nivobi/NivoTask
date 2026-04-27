using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Tasks;

namespace NivoTask.Api.Tests.Columns;

public class DeleteColumnBlockedTests : AuthenticatedTestBase
{
    public DeleteColumnBlockedTests(TestWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task DeleteColumn_WithTasks_Returns400()
    {
        var client = await CreateAuthenticatedClient();

        // Create board (gets 3 default columns)
        var boardResp = await client.PostAsJsonAsync("api/boards",
            new { Name = "DeleteColTest", Color = "#000", Icon = "folder" });
        var board = await boardResp.Content.ReadFromJsonAsync<BoardResponse>();
        var columnId = board!.Columns[0].Id;

        // Create task in first column
        await client.PostAsJsonAsync(
            $"api/boards/{board.Id}/columns/{columnId}/tasks",
            new { Title = "Blocking Task" });

        // Try to delete column with tasks
        var response = await client.DeleteAsync(
            $"api/boards/{board.Id}/columns/{columnId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Move tasks to another column first", body);
    }

    [Fact]
    public async Task DeleteColumn_WithoutTasks_Succeeds()
    {
        var client = await CreateAuthenticatedClient();

        // Create board (gets 3 default columns: To Do, In Progress, Done)
        var boardResp = await client.PostAsJsonAsync("api/boards",
            new { Name = "DeleteColOk", Color = "#000", Icon = "folder" });
        var board = await boardResp.Content.ReadFromJsonAsync<BoardResponse>();

        // Delete the first column (no tasks in it)
        var columnId = board!.Columns[0].Id;
        var response = await client.DeleteAsync(
            $"api/boards/{board.Id}/columns/{columnId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
