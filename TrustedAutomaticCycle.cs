namespace SWCouponManager;

internal sealed record AutomaticCycleResult(int Planned, int Completed, int Failed);

internal sealed class TrustedAutomaticCycle
{
    private readonly AppState _state;
    private readonly AppStorage _storage;
    private readonly Func<DateTimeOffset> _clock;

    internal TrustedAutomaticCycle(AppState state, AppStorage storage, Func<DateTimeOffset>? clock = null)
    {
        _state = state;
        _storage = storage;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal async Task<AutomaticCycleResult> ExecuteAsync(
        TrustedCandidateManifest manifest,
        Func<AutomaticWorkItem, Action, string, CancellationToken, Task<(string status, string message)>> adapter,
        CancellationToken ct)
    {
        var plan = new TrustedAutomaticPlanner(_state, _clock).Build(manifest);
        var coordinator = new RedemptionAttemptCoordinator(_state, _storage, _clock);
        var completed = 0;
        var failed = 0;

        foreach (var item in plan.Items)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string? correlationId = null;
                var result = await coordinator.ExecuteAsync(item.Redemption,
                    submitted => adapter(item, submitted, correlationId!, ct),
                    startedCorrelationId => correlationId = startedCorrelationId);
                Record(item.Redemption, result.status, result.message);
                completed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                failed++;
            }
        }

        return new AutomaticCycleResult(plan.Items.Count, completed, failed);
    }

    private void Record(WorkItem item, string status, string message)
    {
        if (!_state.History.TryGetValue(item.Account.Id, out var perAccount))
            _state.History[item.Account.Id] = perAccount = new(StringComparer.OrdinalIgnoreCase);
        perAccount[item.Code] = new CouponRecord
        {
            Status = status,
            Message = message,
            Time = _clock()
        };
        _storage.Save(_state);
    }
}
