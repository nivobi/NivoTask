using System.Threading;

namespace NivoTask.Client.Services;

public class TimerStateService : IDisposable
{
    public int? ActiveTaskId { get; private set; }
    public string? ActiveTaskTitle { get; private set; }
    public int? ActiveEntryId { get; private set; }
    public DateTime? StartTime { get; private set; }

    public int ElapsedSeconds => StartTime.HasValue
        ? Math.Max(0, (int)(DateTime.UtcNow - StartTime.Value).TotalSeconds)
        : 0;

    public bool IsRunning => ActiveTaskId.HasValue;

    public event Action? OnTimerChanged;

    private Timer? _ticker;

    public void SetActive(int taskId, string taskTitle, int entryId, DateTime startTime)
    {
        ActiveTaskId = taskId;
        ActiveTaskTitle = taskTitle;
        ActiveEntryId = entryId;
        StartTime = startTime;
        StartTicking();
        OnTimerChanged?.Invoke();
    }

    public void Clear()
    {
        ActiveTaskId = null;
        ActiveTaskTitle = null;
        ActiveEntryId = null;
        StartTime = null;
        StopTicking();
        OnTimerChanged?.Invoke();
    }

    private void StartTicking()
    {
        _ticker?.Dispose();
        _ticker = new Timer(_ => OnTimerChanged?.Invoke(), null, 1000, 1000);
    }

    private void StopTicking()
    {
        _ticker?.Dispose();
        _ticker = null;
    }

    public void Dispose() => StopTicking();
}
