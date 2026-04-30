using Microsoft.EntityFrameworkCore;
using NivoTask.Api.Data;

namespace NivoTask.Api.Services;

public class StaleTimerCleanupService : BackgroundService
{
    public static readonly TimeSpan MaxTimerDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleTimerCleanupService> _log;

    public StaleTimerCleanupService(IServiceScopeFactory scopeFactory, ILogger<StaleTimerCleanupService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            await SweepAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = DateTime.UtcNow.Subtract(MaxTimerDuration);
            var stale = await db.TimeEntries
                .Where(te => te.EndTime == null && te.StartTime != null && te.StartTime < cutoff)
                .ToListAsync(ct);
            if (stale.Count == 0) return;

            foreach (var te in stale)
            {
                te.EndTime = te.StartTime!.Value.Add(MaxTimerDuration);
                te.DurationSeconds = (int)MaxTimerDuration.TotalSeconds;
                _log.LogWarning("Auto-closed stale timer {Id} for user {User} (started {Start})",
                    te.Id, te.UserId, te.StartTime);
            }
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "Stale-timer sweep failed");
        }
    }
}
