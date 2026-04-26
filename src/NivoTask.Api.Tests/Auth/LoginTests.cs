using System.Net;
using System.Net.Http.Json;
using NivoTask.Api.Tests.Fixtures;

namespace NivoTask.Api.Tests.Auth;

public class LoginTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LoginTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithCookie()
    {
        // Arrange -- seed user: admin@nivotask.local / Admin12345678
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var loginPayload = new { email = "admin@nivotask.local", password = "Admin12345678" };

        // Act
        var response = await client.PostAsJsonAsync("/login?useCookies=true", loginPayload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"),
            "Response should contain Set-Cookie header");
    }

    [Fact]
    public async Task Login_InvalidPassword_Returns401()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var loginPayload = new { email = "admin@nivotask.local", password = "WrongPassword" };

        var response = await client.PostAsJsonAsync("/login?useCookies=true", loginPayload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_NonExistentUser_Returns401()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var loginPayload = new { email = "nobody@example.com", password = "SomePassword123" };

        var response = await client.PostAsJsonAsync("/login?useCookies=true", loginPayload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Lockout_After5FailedAttempts()
    {
        // Use a separate factory to avoid polluting shared state with lockout
        await using var isolatedFactory = new TestWebApplicationFactory();
        var client = isolatedFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var loginPayload = new { email = "admin@nivotask.local", password = "WrongPassword" };

        // 5 failed attempts
        for (int i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/login?useCookies=true", loginPayload);
        }

        // 6th attempt -- even with correct password
        var correctPayload = new { email = "admin@nivotask.local", password = "Admin12345678" };
        var response = await client.PostAsJsonAsync("/login?useCookies=true", correctPayload);

        // Should be locked out (401)
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
