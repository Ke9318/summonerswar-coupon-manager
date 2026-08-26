namespace SWCouponManager;

internal sealed class BackgroundAgentScheduler : IAsyncDisposable
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<CancellationToken, Task> _runOnce;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _failureBaseDelay;
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _paused;

    internal BackgroundAgentScheduler(
        Func<CancellationToken, Task> runOnce,
        DateTimeOffset? lastCompletedAt = null,
        TimeSpan? interval = null,
        TimeSpan? failureBaseDelay = null,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _runOnce = runOnce;
        LastCompletedAt = lastCompletedAt;
        _interval = interval ?? TimeSpan.FromMinutes(15);
        _failureBaseDelay = failureBaseDelay ?? TimeSpan.FromMinutes(1);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
        if (_interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        if (_failureBaseDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(failureBaseDelay));
    }

    internal DateTimeOffset? LastCompletedAt { get; private set; }
    internal bool IsRunning { get { lock (_sync) return _loop is { IsCompleted: false }; } }
    internal bool IsPaused => _paused;

    internal bool Start()
    {
        lock (_sync)
        {
            if (_loop is { IsCompleted: false }) return false;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _loop = LoopAsync(_cts.Token);
            return true;
        }
    }

    internal void Pause() => _paused = true;
    internal void Resume() => _paused = false;

    internal async Task StopAsync()
    {
        Task? loop;
        lock (_sync)
        {
            _cts?.Cancel();
            loop = _loop;
        }
        if (loop is null) return;
        try { await loop; }
        catch (OperationCanceledException) { }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var failures = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (_paused)
            {
                await _delay(TimeSpan.FromSeconds(1), ct);
                continue;
            }

            var now = _clock();
            var dueAt = LastCompletedAt?.Add(_interval) ?? now;
            if (dueAt > now)
            {
                await _delay(dueAt - now, ct);
                continue;
            }

            try
            {
                await _runOnce(ct);
                LastCompletedAt = _clock();
                failures = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                failures++;
                var exponent = Math.Min(failures - 1, 6);
                var backoffTicks = Math.Min(_interval.Ticks,
                    (long)(_failureBaseDelay.Ticks * Math.Pow(2, exponent)));
                await _delay(TimeSpan.FromTicks(backoffTicks), ct);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        lock (_sync)
        {
            _cts?.Dispose();
            _cts = null;
            _loop = null;
        }
    }
}
