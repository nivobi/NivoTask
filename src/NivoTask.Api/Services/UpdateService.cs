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
    private readonly bool _allowIisSelfUpdate;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private UpdateCheckResponse? _cached;
    private DateTime _cachedAt;

    public UpdateService(
        IHttpClientFactory httpFactory,
        ILogger<UpdateService> log,
        IHostApplicationLifetime lifetime,
        IConfiguration config)
    {
        _httpFactory = httpFactory;
        _log = log;
        _lifetime = lifetime;
        _allowIisSelfUpdate = config.GetValue<bool>("AllowIisSelfUpdate");
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

        var stage = "init";
        try
        {
            // IIS detection: ANCM sets these env vars for every hosted .NET process.
            var isIis = Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH") is not null
                        || Environment.GetEnvironmentVariable("APP_POOL_ID") is not null;

            if (isIis && !_allowIisSelfUpdate)
            {
                return new UpdateStartResponse
                {
                    Status = "manual-required",
                    Stage = "iis-opt-in-required",
                    Message = "Self-update is disabled on IIS by default. Add \"AllowIisSelfUpdate\": true to setup.json (and ensure the app pool identity has Modify on the install dir) to enable."
                };
            }

            // Preflight: install dir must be writable by the running process so xcopy can succeed later.
            if (isIis)
            {
                stage = "preflight-permissions";
                var probeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var probePath = Path.Combine(probeDir, $".nivotask-write-probe-{Guid.NewGuid():N}");
                try
                {
                    await File.WriteAllTextAsync(probePath, "ok", ct);
                    File.Delete(probePath);
                }
                catch (Exception ex)
                {
                    return new UpdateStartResponse
                    {
                        Status = "manual-required",
                        Stage = "preflight-permissions",
                        Message = $"Install dir not writable by app pool identity: {ex.Message}",
                        ExceptionType = ex.GetType().Name
                    };
                }
            }

            stage = "check";
            var check = await CheckAsync(useCache: false, ct);
            if (!check.IsUpdateAvailable)
                return new UpdateStartResponse { Status = "already-current", Message = "Already on latest version" };
            if (string.IsNullOrEmpty(check.AssetUrl))
                return new UpdateStartResponse
                {
                    Status = "no-asset",
                    Message = $"No release asset for runtime '{GetCurrentRid()}'"
                };

            stage = "prepare";
            var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Use OS temp dir — guaranteed writable by the running process across hosting models.
            var workDir = Path.Combine(Path.GetTempPath(), "nivotask-update");
            Directory.CreateDirectory(workDir);
            var stagingDir = Path.Combine(workDir, "staging");
            var archivePath = Path.Combine(workDir, check.AssetName ?? "nivotask-update.zip");

            // Clean staging
            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
            Directory.CreateDirectory(stagingDir);
            if (File.Exists(archivePath)) File.Delete(archivePath);

            stage = "download";
            _log.LogInformation("Downloading update {Asset} to {Path}", check.AssetName, archivePath);
            var http = _httpFactory.CreateClient("github");
            using (var resp = await http.GetAsync(check.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(archivePath);
                await resp.Content.CopyToAsync(fs, ct);
            }

            stage = "extract";
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

            stage = "script";
            // Where the updater script lives and runs from. Some hosts (notably shared IIS)
            // enforce SRP/AppLocker rules that block .bat execution from %TEMP%; the install
            // dir is always allowed because the host already runs NivoTask.Api.exe from there.
            var scriptDir = isIis ? installDir : workDir;

            if (isIis)
            {
                // Copy the offline page template into install dir so the bat can drop it
                // without depending on %TEMP% being readable across process boundaries.
                var template = Path.Combine(installDir, "wwwroot", "app_offline.template.htm");
                var offlineCopy = Path.Combine(installDir, "app_offline.template.htm");
                if (File.Exists(template) && !string.Equals(template, offlineCopy, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(template, offlineCopy, overwrite: true);
                }
                else if (!File.Exists(offlineCopy))
                {
                    await File.WriteAllTextAsync(offlineCopy,
                        "<!doctype html><meta http-equiv=\"refresh\" content=\"5\"><title>Updating…</title><h1>NivoTask is updating</h1>",
                        ct);
                }
            }
            var updaterPath = WriteUpdaterScript(installDir, stagingDir, scriptDir, isIis);
            _log.LogInformation("Spawning updater {Path}", updaterPath);

            stage = "spawn";
            var psi = new ProcessStartInfo
            {
                FileName = updaterPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WorkingDirectory = scriptDir
            };
            Process.Start(psi);

            // Non-IIS: schedule explicit shutdown so the bat's `start exe` can take over.
            // IIS: ANCM stops the worker as soon as the bat drops app_offline.htm — no explicit stop.
            if (!isIis)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    _lifetime.StopApplication();
                }, CancellationToken.None);
            }

            return new UpdateStartResponse
            {
                Status = "started",
                Message = $"Updating to v{check.LatestVersion}. The app will restart shortly.",
                AssetName = check.AssetName
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "Update failed at stage {Stage}", stage);
            return new UpdateStartResponse
            {
                Status = "error",
                Message = ex.Message,
                ExceptionType = ex.GetType().Name,
                Stage = stage
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string WriteUpdaterScript(string installDir, string stagingDir, string scriptDir, bool useIisFlow)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var bat = Path.Combine(scriptDir, "nivotask-updater.bat");
            string content;
            if (useIisFlow)
            {
                // IIS flow: drop app_offline.htm so ANCM stops worker (releases DLL locks),
                // xcopy over install dir, remove app_offline.htm so ANCM respawns worker.
                // On failure, leave app_offline.htm with an error message in place.
                content = $"""
                    @echo off
                    setlocal enabledelayedexpansion
                    timeout /t 3 /nobreak >nul
                    copy /Y "{installDir}\app_offline.template.htm" "{installDir}\app_offline.htm" >nul
                    timeout /t 4 /nobreak >nul
                    xcopy "{stagingDir}\*" "{installDir}\" /E /Y /I /Q
                    if !errorlevel! neq 0 (
                        echo ^<!doctype html^>^<meta charset="utf-8"^>^<title^>Update failed^</title^>^<h1^>NivoTask update failed^</h1^>^<p^>xcopy returned !errorlevel!. Restore from backup, then delete app_offline.htm to recover.^</p^> > "{installDir}\app_offline.htm"
                        exit /b 1
                    )
                    if not exist "{installDir}\NivoTask.Api.exe" (
                        echo ^<!doctype html^>^<meta charset="utf-8"^>^<title^>Update failed^</title^>^<h1^>NivoTask update failed^</h1^>^<p^>NivoTask.Api.exe missing after xcopy.^</p^> > "{installDir}\app_offline.htm"
                        exit /b 1
                    )
                    rmdir /s /q "{stagingDir}"
                    del /f /q "{installDir}\app_offline.htm"
                    del /f /q "%~f0"
                    """;
            }
            else
            {
                content = $"""
                    @echo off
                    timeout /t 3 /nobreak >nul
                    xcopy "{stagingDir}\*" "{installDir}\" /E /Y /I /Q
                    rmdir /s /q "{stagingDir}"
                    start "NivoTask" "{installDir}\NivoTask.Api.exe"
                    del "%~f0"
                    """;
            }
            File.WriteAllText(bat, content);
            return bat;
        }
        else
        {
            var sh = Path.Combine(scriptDir, "nivotask-updater.sh");
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
