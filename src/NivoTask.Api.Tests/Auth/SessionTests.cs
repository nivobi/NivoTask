using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;

namespace NivoTask.Api.Tests.Auth;

public class SessionTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SessionTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UnauthenticatedRequest_Returns401()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Protected endpoint should return 401 (fallback policy requires auth)
        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedRequest_WithCookie_Returns200()
    {
        // CreateClient() preserves cookies across requests by default (HandleCookies = true)
        var client = _factory.CreateClient();

        // Login first
        var loginPayload = new { email = "admin@nivotask.local", password = "Admin12345678" };
        var loginResponse = await client.PostAsJsonAsync("/login?useCookies=true", loginPayload);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Now access protected endpoint -- cookie should be sent automatically
        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
