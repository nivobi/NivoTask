using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;
using NivoTask.Shared.Dtos.Boards;

namespace NivoTask.Api.Tests.Boards;

public class BoardCrudTests : AuthenticatedTestBase
{
    public BoardCrudTests(TestWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task CreateBoard_ValidRequest_Returns201WithDefaultColumns()
    {
        var client = await CreateAuthenticatedClient();
        var request = new CreateBoardRequest { Name = "Test Board", Color = "#FF0000", Icon = "star" };

        var response = await client.PostAsJsonAsync("/api/boards", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var board = await response.Content.ReadFromJsonAsync<BoardResponse>();
        Assert.NotNull(board);
        Assert.Equal("Test Board", board.Name);
        Assert.Equal("#FF0000", board.Color);
        Assert.Equal("star", board.Icon);
        Assert.Equal(3, board.Columns.Count);
        Assert.Contains(board.Columns, c => c.Name == "To Do" && !c.IsDone);
        Assert.Contains(board.Columns, c => c.Name == "In Progress" && !c.IsDone);
        Assert.Contains(board.Columns, c => c.Name == "Done" && c.IsDone);
    }

    [Fact]
    public async Task GetBoards_AfterCreation_ReturnsBoardList()
    {
        var client = await CreateAuthenticatedClient();
        var request = new CreateBoardRequest { Name = "ListTest Board" };
        await client.PostAsJsonAsync("/api/boards", request);

        var response = await client.GetAsync("/api/boards");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var boards = await response.Content.ReadFromJsonAsync<List<BoardSummaryResponse>>();
        Assert.NotNull(boards);
        var created = boards.FirstOrDefault(b => b.Name == "ListTest Board");
        Assert.NotNull(created);
        Assert.Equal(3, created.ColumnCount);
    }

    [Fact]
    public async Task GetBoard_ById_ReturnsBoardWithColumns()
    {
        var client = await CreateAuthenticatedClient();
        var createRequest = new CreateBoardRequest { Name = "DetailTest Board" };
        var createResponse = await client.PostAsJsonAsync("/api/boards", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<BoardResponse>();

        var response = await client.GetAsync($"/api/boards/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var board = await response.Content.ReadFromJsonAsync<BoardResponse>();
        Assert.NotNull(board);
        Assert.Equal("DetailTest Board", board.Name);
        Assert.Equal(3, board.Columns.Count);
        // Verify columns are ordered by SortOrder
        var sortOrders = board.Columns.Select(c => c.SortOrder).ToList();
        Assert.Equal(sortOrders.OrderBy(s => s).ToList(), sortOrders);
    }

    [Fact]
    public async Task UpdateBoard_ValidRequest_Returns204()
    {
        var client = await CreateAuthenticatedClient();
        var createRequest = new CreateBoardRequest { Name = "UpdateTest Board" };
        var createResponse = await client.PostAsJsonAsync("/api/boards", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<BoardResponse>();

        var updateRequest = new UpdateBoardRequest { Name = "Updated Name", Color = "#00FF00", Icon = "edit" };
        var updateResponse = await client.PutAsJsonAsync($"/api/boards/{created!.Id}", updateRequest);

        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        // Verify the update persisted
        var getResponse = await client.GetAsync($"/api/boards/{created.Id}");
        var board = await getResponse.Content.ReadFromJsonAsync<BoardResponse>();
        Assert.Equal("Updated Name", board!.Name);
        Assert.Equal("#00FF00", board.Color);
        Assert.Equal("edit", board.Icon);
    }

    [Fact]
    public async Task DeleteBoard_ExistingBoard_Returns204()
    {
        // Use isolated factory to avoid affecting other tests
        await using var isolatedFactory = new TestWebApplicationFactory();
        var client = isolatedFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var loginPayload = new { email = "admin@nivotask.local", password = "Admin12345678" };
        await client.PostAsJsonAsync("/login?useCookies=true", loginPayload);

        var createRequest = new CreateBoardRequest { Name = "DeleteTest Board" };
        var createResponse = await client.PostAsJsonAsync("/api/boards", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<BoardResponse>();

        var deleteResponse = await client.DeleteAsync($"/api/boards/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/boards/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateBoard_EmptyName_Returns400()
    {
        var client = await CreateAuthenticatedClient();
        var request = new CreateBoardRequest { Name = "" };

        var response = await client.PostAsJsonAsync("/api/boards", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBoard_NonExistentId_Returns404()
    {
        var client = await CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/boards/99999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
