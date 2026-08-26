namespace SWCouponManager;

internal sealed class RedemptionAttemptCoordinator
{
    private readonly AppState _state;
    private readonly AppStorage _storage;
    private readonly AttemptJournal _journal;
    private readonly TimeSpan _baseRetryDelay;

    internal RedemptionAttemptCoordinator(
        AppState state,
        AppStorage storage,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? baseRetryDelay = null)
    {
        _state = state;
        _storage = storage;
        _journal = new AttemptJournal(state, clock);
        _baseRetryDelay = baseRetryDelay ?? TimeSpan.FromMinutes(5);
    }

    internal bool CanQueue(WorkItem item) =>
        _journal.CanQueue(item.Account.Id, item.Code, _state.History);

    internal async Task<(string status, string message)> ExecuteAsync(
        WorkItem item,
        Func<Action, Task<(string status, string message)>> redeem,
        Action<string>? attemptStarted = null)
    {
        var attempt = _journal.Queue(item.Account.Id, item.Code);
        _storage.Save(_state);
        attemptStarted?.Invoke(attempt.CorrelationId);

        _journal.MarkExecuting(item.Account.Id, item.Code);
        _storage.Save(_state);

        var submitted = false;
        void MarkSubmitted()
        {
            if (submitted) return;
            _journal.MarkVerifying(item.Account.Id, item.Code);
            _storage.Save(_state);
            submitted = true;
        }

        try
        {
            var result = await redeem(MarkSubmitted);
            Complete(item, result.status, submitted);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (submitted) _journal.MarkAmbiguous(item.Account.Id, item.Code);
            else _journal.MarkTemporaryFailure(item.Account.Id, item.Code, _baseRetryDelay);
            _storage.Save(_state);
            throw;
        }
        catch
        {
            if (submitted) _journal.MarkAmbiguous(item.Account.Id, item.Code);
            else _journal.MarkTemporaryFailure(item.Account.Id, item.Code, _baseRetryDelay);
            _storage.Save(_state);
            throw;
        }
    }

    private void Complete(WorkItem item, string status, bool submitted)
    {
        if (MainForm.IsCompletedStatus(status))
            _journal.MarkTerminal(item.Account.Id, item.Code, status);
        else if (status == "error" && !submitted)
            _journal.MarkTemporaryFailure(item.Account.Id, item.Code, _baseRetryDelay);
        else
            _journal.MarkAmbiguous(item.Account.Id, item.Code);
        _storage.Save(_state);
    }
}
