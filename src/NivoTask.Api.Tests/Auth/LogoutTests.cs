using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;

namespace NivoTask.Api.Tests.Auth;

public class LogoutTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LogoutTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Logout_ClearsCookie_SubsequentRequestReturns401()
    {
        // CreateClient() preserves cookies across requests by default (HandleCookies = true)
        var client = _factory.CreateClient();

        // Login
        var loginPayload = new { email = "admin@nivotask.local", password = "Admin12345678" };
        var loginResponse = await client.PostAsJsonAsync("/login?useCookies=true", loginPayload);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Verify authenticated
        var authCheck = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, authCheck.StatusCode);

        // Logout
        var logoutResponse = await client.PostAsJsonAsync("/logout", new { });
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        // Subsequent request should fail
        var afterLogout = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Logout_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.PostAsJsonAsync("/logout", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
