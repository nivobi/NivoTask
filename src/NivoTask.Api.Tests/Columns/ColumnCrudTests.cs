using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Columns;

namespace NivoTask.Api.Tests.Columns;

public class ColumnCrudTests : AuthenticatedTestBase
{
    public ColumnCrudTests(TestWebApplicationFactory factory) : base(factory) { }

    private async Task<(HttpClient client, BoardResponse board)> CreateBoardAsync()
    {
        var client = await CreateAuthenticatedClient();
        var request = new CreateBoardRequest { Name = $"ColTest-{Guid.NewGuid():N}" };
        var response = await client.PostAsJsonAsync("/api/boards", request);
        response.EnsureSuccessStatusCode();
        var board = await response.Content.ReadFromJsonAsync<BoardResponse>();
        return (client, board!);
    }

    [Fact]
    public async Task CreateColumn_ValidRequest_Returns201()
    {
        var (client, board) = await CreateBoardAsync();
        var request = new CreateColumnRequest { Name = "Review", IsDone = false };

        var response = await client.PostAsJsonAsync($"/api/boards/{board.Id}/columns", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var column = await response.Content.ReadFromJsonAsync<ColumnResponse>();
        Assert.NotNull(column);
        Assert.Equal("Review", column.Name);
        Assert.False(column.IsDone);
        Assert.Equal(board.Id, column.BoardId);
    }

    [Fact]
    public async Task UpdateColumn_SetIsDone_ClearsOtherIsDone()
    {
        var (client, board) = await CreateBoardAsync();

        // Create a new column with IsDone=true
        var request = new CreateColumnRequest { Name = "New Done", IsDone = true };
        await client.PostAsJsonAsync($"/api/boards/{board.Id}/columns", request);

        // Get all columns and verify only the new column has IsDone=true
        var getResponse = await client.GetAsync($"/api/boards/{board.Id}/columns");
        var columns = await getResponse.Content.ReadFromJsonAsync<List<ColumnResponse>>();

        Assert.NotNull(columns);
        var doneColumns = columns.Where(c => c.IsDone).ToList();
        Assert.Single(doneColumns);
        Assert.Equal("New Done", doneColumns[0].Name);
    }

    [Fact]
    public async Task DeleteColumn_LastColumn_Returns400()
    {
        var (client, board) = await CreateBoardAsync();

        // Delete 2 of 3 default columns
        var columnsResponse = await client.GetAsync($"/api/boards/{board.Id}/columns");
        var columns = await columnsResponse.Content.ReadFromJsonAsync<List<ColumnResponse>>();
        Assert.NotNull(columns);
        Assert.Equal(3, columns.Count);

        await client.DeleteAsync($"/api/boards/{board.Id}/columns/{columns[0].Id}");
        await client.DeleteAsync($"/api/boards/{board.Id}/columns/{columns[1].Id}");

        // Try to delete the last column
        var response = await client.DeleteAsync($"/api/boards/{board.Id}/columns/{columns[2].Id}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteColumn_ExistingColumn_Returns204()
    {
        var (client, board) = await CreateBoardAsync();

        var columnsResponse = await client.GetAsync($"/api/boards/{board.Id}/columns");
        var columns = await columnsResponse.Content.ReadFromJsonAsync<List<ColumnResponse>>();
        Assert.NotNull(columns);

        var response = await client.DeleteAsync($"/api/boards/{board.Id}/columns/{columns[0].Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify only 2 remain
        var afterResponse = await client.GetAsync($"/api/boards/{board.Id}/columns");
        var afterColumns = await afterResponse.Content.ReadFromJsonAsync<List<ColumnResponse>>();
        Assert.Equal(2, afterColumns!.Count);
    }

    [Fact]
    public async Task UpdateColumn_Rename_Returns204()
    {
        var (client, board) = await CreateBoardAsync();

        var columnsResponse = await client.GetAsync($"/api/boards/{board.Id}/columns");
        var columns = await columnsResponse.Content.ReadFromJsonAsync<List<ColumnResponse>>();
        var column = columns![0];

        var updateRequest = new UpdateColumnRequest { Name = "Renamed Column", IsDone = column.IsDone };
        var response = await client.PutAsJsonAsync($"/api/boards/{board.Id}/columns/{column.Id}", updateRequest);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
