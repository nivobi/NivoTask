using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NivoTask.Api.Tests.Fixtures;

public abstract class AuthenticatedTestBase : IClassFixture<TestWebApplicationFactory>
{
    protected readonly TestWebApplicationFactory Factory;

    protected AuthenticatedTestBase(TestWebApplicationFactory factory)
    {
        Factory = factory;
    }

    protected async Task<HttpClient> CreateAuthenticatedClient()
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var loginPayload = new { email = "admin@nivotask.local", password = "Admin12345678" };
        var loginResponse = await client.PostAsJsonAsync("/login?useCookies=true", loginPayload);
        loginResponse.EnsureSuccessStatusCode();

        return client;
    }
}
