using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NivoTask.Api.Services;

namespace NivoTask.Api.Tests.Services;

public class UpdateServiceIisTests
{
    private sealed class EnvScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new();

        public EnvScope Set(string name, string? value)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var (k, v) in _previous)
                Environment.SetEnvironmentVariable(k, v);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private static UpdateService Build(bool allowIisSelfUpdate)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowIisSelfUpdate"] = allowIisSelfUpdate ? "true" : "false"
            })
            .Build();
        return new UpdateService(
            new StubHttpClientFactory(),
            NullLogger<UpdateService>.Instance,
            new StubLifetime(),
            config);
    }

    [Fact]
    public async Task OnIis_WithFlagFalse_RefusesWithIisOptInRequired()
    {
        using var env = new EnvScope().Set("ASPNETCORE_IIS_PHYSICAL_PATH", @"C:\fake");
        var svc = Build(allowIisSelfUpdate: false);

        var result = await svc.StartUpdateAsync(CancellationToken.None);

        Assert.Equal("manual-required", result.Status);
        Assert.Equal("iis-opt-in-required", result.Stage);
        Assert.Contains("AllowIisSelfUpdate", result.Message);
    }

    [Fact]
    public async Task OnIis_WithFlagTrue_AndWritableInstallDir_PassesPreflight()
    {
        // AppContext.BaseDirectory under xUnit is the test project's bin dir — writable.
        using var env = new EnvScope()
            .Set("ASPNETCORE_IIS_PHYSICAL_PATH", @"C:\fake");
        var svc = Build(allowIisSelfUpdate: true);

        var result = await svc.StartUpdateAsync(CancellationToken.None);

        // Preflight passes; flow proceeds to GitHub check which fails (offline) and reports
        // either error (network) or no-asset / already-current. Critical: NOT
        // iis-opt-in-required and NOT preflight-permissions.
        Assert.NotEqual("iis-opt-in-required", result.Stage);
        Assert.NotEqual("preflight-permissions", result.Stage);
    }

    [Fact]
    public async Task NotIis_FlagIgnored_ProceedsPastGate()
    {
        using var env = new EnvScope()
            .Set("ASPNETCORE_IIS_PHYSICAL_PATH", null)
            .Set("APP_POOL_ID", null);
        var svc = Build(allowIisSelfUpdate: false);

        var result = await svc.StartUpdateAsync(CancellationToken.None);

        // Same as above: must NOT hit the IIS gate or preflight. Anything else (error
        // from GitHub call, already-current, etc.) is fine here.
        Assert.NotEqual("iis-opt-in-required", result.Stage);
        Assert.NotEqual("preflight-permissions", result.Stage);
    }
}
