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
            if (isIis)
            {
                // Copy the offline page template into install dir so the inline cmd can drop it.
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
            var spawnInfo = BuildUpdaterSpawn(installDir, stagingDir, workDir, isIis);
            _log.LogInformation("Spawning updater: {File} {Args}", spawnInfo.FileName, spawnInfo.Arguments);

            stage = "spawn";
            var psi = new ProcessStartInfo
            {
                FileName = spawnInfo.FileName,
                Arguments = spawnInfo.Arguments,
                UseShellExecute = true,
                CreateNoWindow = true,
                WorkingDirectory = installDir
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

    private record SpawnInfo(string FileName, string Arguments);

    private static SpawnInfo BuildUpdaterSpawn(string installDir, string stagingDir, string workDir, bool useIisFlow)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Use ping for sleep — `timeout` requires an interactive console and fails
            // under non-interactive cmd. -n 4 yields ~3 seconds.
            string command;
            if (useIisFlow)
            {
                // Group policy on shared IIS hosts (e.g. Simply.com) blocks .bat execution
                // from any user-writable directory. cmd.exe + system tools (xcopy, copy, del)
                // are whitelisted, so we run the whole sequence inline through cmd.exe with
                // no script file written to disk.
                //
                // Sequence:
                //   - sleep ~3s for HTTP response to flush
                //   - drop app_offline.htm (ANCM detects, gracefully shuts worker, releases locks)
                //   - sleep ~5s for shutdown
                //   - xcopy staging → install
                //   - cleanup staging, remove app_offline.htm (ANCM respawns worker)
                //
                // Chained with `&&` so a failure leaves app_offline.htm in place; user sees
                // the "Updating…" template and knows to delete it manually if it lingers.
                command = string.Join(" && ", new[]
                {
                    "ping 127.0.0.1 -n 4 >nul",
                    $"copy /Y \"{installDir}\\app_offline.template.htm\" \"{installDir}\\app_offline.htm\" >nul",
                    "ping 127.0.0.1 -n 6 >nul",
                    $"xcopy \"{stagingDir}\\*\" \"{installDir}\\\" /E /Y /I /Q",
                    $"rmdir /s /q \"{stagingDir}\"",
                    $"del /f /q \"{installDir}\\app_offline.htm\""
                });
            }
            else
            {
                // Standalone (non-IIS) Windows: bring the new exe up ourselves after the swap.
                command = string.Join(" && ", new[]
                {
                    "ping 127.0.0.1 -n 4 >nul",
                    $"xcopy \"{stagingDir}\\*\" \"{installDir}\\\" /E /Y /I /Q",
                    $"rmdir /s /q \"{stagingDir}\"",
                    $"start \"NivoTask\" \"{installDir}\\NivoTask.Api.exe\""
                });
            }
            return new SpawnInfo("cmd.exe", "/c " + command);
        }
        else
        {
            // Unix: still write a shell script (no equivalent group-policy issue) and
            // invoke /bin/sh against it so the file's exec bit doesn't matter.
            var sh = Path.Combine(workDir, "nivotask-updater.sh");
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
            return new SpawnInfo("/bin/sh", $"\"{sh}\"");
        }
    }

    private static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(latest, out var l) && Version.TryParse(current, out var c))
            return l > c;
        return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
    }
}
