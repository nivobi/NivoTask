using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using NivoTask.Shared.Dtos.System;

namespace NivoTask.Api.Services;

public class UpdateService
{
    private const string Owner = "nivobi";
    private const string Repo = "NivoTask";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<UpdateService> _log;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private UpdateCheckResponse? _cached;
    private DateTime _cachedAt;

    public UpdateService(IHttpClientFactory httpFactory, ILogger<UpdateService> log, IHostApplicationLifetime lifetime)
    {
        _httpFactory = httpFactory;
        _log = log;
        _lifetime = lifetime;
    }

    public string GetCurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly();
        var info = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Strip git build metadata if present (e.g. "0.1.0+abcdef")
            var plus = info.IndexOf('+');
            if (plus > 0) info = info[..plus];
            return info!;
        }
        return asm?.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public string GetCurrentRid() => RuntimeInformation.RuntimeIdentifier;

    public VersionInfoResponse GetVersionInfo() => new()
    {
        Version = GetCurrentVersion(),
        Runtime = GetCurrentRid(),
        Channel = "release"
    };

    public async Task<UpdateCheckResponse> CheckAsync(bool useCache, CancellationToken ct)
    {
        if (useCache && _cached is not null && DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(10))
            return _cached;

        var current = GetCurrentVersion();
        var rid = GetCurrentRid();
        var response = new UpdateCheckResponse { CurrentVersion = current };

        try
        {
            var http = _httpFactory.CreateClient("github");
            using var resp = await http.GetAsync($"repos/{Owner}/{Repo}/releases/latest", ct);
            if (!resp.IsSuccessStatusCode)
            {
                response.Error = $"GitHub returned {(int)resp.StatusCode}";
                return response;
            }
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var latest = tag.StartsWith('v') ? tag[1..] : tag;
            response.LatestVersion = latest;
            response.ReleaseUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            response.ReleaseNotes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            response.PublishedAt = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
                ? DateTime.Parse(p.GetString()!).ToUniversalTime() : null;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.Contains($"-{rid}.", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith($"-{rid}.zip", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith($"-{rid}.tar.gz", StringComparison.OrdinalIgnoreCase))
                    {
                        response.AssetName = name;
                        response.AssetUrl = asset.GetProperty("browser_download_url").GetString();
                        response.AssetSizeBytes = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                        break;
                    }
                }
            }

            response.IsUpdateAvailable = IsNewer(latest, current);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Update check failed");
            response.Error = ex.Message;
        }

        _cached = response;
        _cachedAt = DateTime.UtcNow;
        return response;
    }

    public async Task<UpdateStartResponse> StartUpdateAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct))
            return new UpdateStartResponse { Status = "in-progress", Message = "Update already in progress" };

        try
        {
            var check = await CheckAsync(useCache: false, ct);
            if (!check.IsUpdateAvailable)
                return new UpdateStartResponse { Status = "already-current", Message = "Already on latest version" };
            if (string.IsNullOrEmpty(check.AssetUrl))
                return new UpdateStartResponse
                {
                    Status = "no-asset",
                    Message = $"No release asset for runtime '{GetCurrentRid()}'"
                };

            var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parentDir = Path.GetDirectoryName(installDir) ?? installDir;
            var stagingDir = Path.Combine(parentDir, "nivotask-update-staging");
            var archivePath = Path.Combine(parentDir, check.AssetName ?? "nivotask-update.zip");

            // Clean staging
            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
            Directory.CreateDirectory(stagingDir);
            if (File.Exists(archivePath)) File.Delete(archivePath);

            // Download
            _log.LogInformation("Downloading update {Asset} to {Path}", check.AssetName, archivePath);
            var http = _httpFactory.CreateClient("github");
            using (var resp = await http.GetAsync(check.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(archivePath);
                await resp.Content.CopyToAsync(fs, ct);
            }

            // Extract
            _log.LogInformation("Extracting to {Staging}", stagingDir);
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archivePath, stagingDir, overwriteFiles: true);
            }
            else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                await using var fs = File.OpenRead(archivePath);
                await using var gz = new GZipStream(fs, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gz, stagingDir, overwriteFiles: true, cancellationToken: ct);
            }
            else
            {
                return new UpdateStartResponse { Status = "no-asset", Message = "Unknown archive format" };
            }

            File.Delete(archivePath);

            // Write updater script
            var updaterPath = WriteUpdaterScript(installDir, stagingDir);
            _log.LogInformation("Spawning updater {Path}", updaterPath);

            var psi = new ProcessStartInfo
            {
                FileName = updaterPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WorkingDirectory = parentDir
            };
            Process.Start(psi);

            // Schedule shutdown so the response is flushed first
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                _lifetime.StopApplication();
            }, CancellationToken.None);

            return new UpdateStartResponse
            {
                Status = "started",
                Message = $"Updating to v{check.LatestVersion}. The app will restart shortly.",
                AssetName = check.AssetName
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string WriteUpdaterScript(string installDir, string stagingDir)
    {
        var parentDir = Path.GetDirectoryName(installDir)!;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var bat = Path.Combine(parentDir, "nivotask-updater.bat");
            var content = $"""
                @echo off
                timeout /t 3 /nobreak >nul
                xcopy "{stagingDir}\*" "{installDir}\" /E /Y /I /Q
                rmdir /s /q "{stagingDir}"
                start "NivoTask" "{installDir}\NivoTask.Api.exe"
                del "%~f0"
                """;
            File.WriteAllText(bat, content);
            return bat;
        }
        else
        {
            var sh = Path.Combine(parentDir, "nivotask-updater.sh");
            var exeName = "NivoTask.Api";
            var content = $"""
                #!/bin/sh
                sleep 3
                cp -R "{stagingDir}/." "{installDir}/"
                rm -rf "{stagingDir}"
                chmod +x "{installDir}/{exeName}"
                nohup "{installDir}/{exeName}" >/dev/null 2>&1 &
                rm -- "$0"
                """;
            File.WriteAllText(sh, content);
            try
            {
                File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                          UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                          UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch { }
            return sh;
        }
    }

    private static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(latest, out var l) && Version.TryParse(current, out var c))
            return l > c;
        return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
    }
}
