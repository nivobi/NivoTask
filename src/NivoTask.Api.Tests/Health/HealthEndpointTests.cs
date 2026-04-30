using System.Net;
using System.Text.Json;
using NivoTask.Api.Tests.Fixtures;

namespace NivoTask.Api.Tests.Health;

public class HealthEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public HealthEndpointTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Healthz_Anonymous_Returns200WithStatusVersionAndChecks()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("status", out var status));
        Assert.Equal("Healthy", status.GetString());
        Assert.True(root.TryGetProperty("version", out var version));
        Assert.False(string.IsNullOrWhiteSpace(version.GetString()));
        Assert.True(root.TryGetProperty("hostname", out var hostname));
        Assert.False(string.IsNullOrWhiteSpace(hostname.GetString()));
        Assert.True(root.TryGetProperty("uptimeSeconds", out var uptime));
        Assert.True(uptime.GetInt64() >= 0);
        Assert.True(root.TryGetProperty("checks", out var checks));
        Assert.True(checks.TryGetProperty("database", out var dbCheck));
        Assert.Equal("Healthy", dbCheck.GetString());
    }
}
