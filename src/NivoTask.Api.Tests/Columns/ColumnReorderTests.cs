using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;
using NivoTask.Shared.Dtos.Columns;

namespace NivoTask.Api.Tests.Columns;

public class ColumnReorderTests : AuthenticatedTestBase
{
    public ColumnReorderTests(TestWebApplicationFactory factory) : base(factory) { }

    private async Task<(HttpClient client, BoardResponse board)> CreateBoardAsync()
    {
        var client = await CreateAuthenticatedClient();
        var request = new CreateBoardRequest { Name = $"ReorderTest-{Guid.NewGuid():N}" };
        var response = await client.PostAsJsonAsync("/api/boards", request);
        response.EnsureSuccessStatusCode();
        var board = await response.Content.ReadFromJsonAsync<BoardResponse>();
        return (client, board!);
    }

    [Fact]
    public async Task ReorderColumns_ValidOrder_Returns204()
    {
        var (client, board) = await CreateBoardAsync();

        // Get current columns
        var getResponse = await client.GetAsync($"/api/boards/{board.Id}/columns");
        var columns = await getResponse.Content.ReadFromJsonAsync<List<ColumnResponse>>();
        Assert.NotNull(columns);
        Assert.Equal(3, columns.Count);

        // Reverse the order
        var reversedIds = columns.Select(c => c.Id).Reverse().ToList();
        var reorderRequest = new ReorderColumnsRequest { ColumnIds = reversedIds };

        var response = await client.PatchAsJsonAsync($"/api/boards/{board.Id}/columns/reorder", reorderRequest);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify new order
        var afterResponse = await client.GetAsync($"/api/boards/{board.Id}/columns");
        var afterColumns = await afterResponse.Content.ReadFromJsonAsync<List<ColumnResponse>>();
        Assert.NotNull(afterColumns);

        // First column should now be what was last (Done), etc.
        Assert.Equal(reversedIds[0], afterColumns[0].Id);
        Assert.Equal(reversedIds[1], afterColumns[1].Id);
        Assert.Equal(reversedIds[2], afterColumns[2].Id);
        // Verify SortOrder is ascending
        Assert.True(afterColumns[0].SortOrder < afterColumns[1].SortOrder);
        Assert.True(afterColumns[1].SortOrder < afterColumns[2].SortOrder);
    }

    [Fact]
    public async Task ReorderColumns_MissingColumnId_Returns400()
    {
        var (client, board) = await CreateBoardAsync();

        var getResponse = await client.GetAsync($"/api/boards/{board.Id}/columns");
        var columns = await getResponse.Content.ReadFromJsonAsync<List<ColumnResponse>>();
        Assert.NotNull(columns);

        // Only provide 2 of 3 column IDs
        var partialIds = columns.Take(2).Select(c => c.Id).ToList();
        var reorderRequest = new ReorderColumnsRequest { ColumnIds = partialIds };

        var response = await client.PatchAsJsonAsync($"/api/boards/{board.Id}/columns/reorder", reorderRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReorderColumns_InvalidColumnId_Returns400()
    {
        var (client, board) = await CreateBoardAsync();

        var getResponse = await client.GetAsync($"/api/boards/{board.Id}/columns");
        var columns = await getResponse.Content.ReadFromJsonAsync<List<ColumnResponse>>();
        Assert.NotNull(columns);

        // Replace one ID with a non-existent one
        var invalidIds = columns.Select(c => c.Id).ToList();
        invalidIds[0] = 99999;
        var reorderRequest = new ReorderColumnsRequest { ColumnIds = invalidIds };

        var response = await client.PatchAsJsonAsync($"/api/boards/{board.Id}/columns/reorder", reorderRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
