namespace SWCouponManager;

internal sealed class AttemptJournal
{
    private readonly AppState _state;
    private readonly Func<DateTimeOffset> _clock;

    internal AttemptJournal(AppState state, Func<DateTimeOffset>? clock = null)
    {
        _state = state;
        _state.Attempts ??= [];
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal bool CanQueue(string accountId, string code,
        Dictionary<string, Dictionary<string, CouponRecord>> history,
        int maxTemporaryFailures = 3)
    {
        var normalized = NormalizeCode(code);
        if (!MainForm.ShouldProcess(history, accountId, normalized)) return false;
        if (!TryGet(accountId, normalized, out var current)) return true;

        if (current.Status is AttemptStatus.Queued or AttemptStatus.Executing or
            AttemptStatus.Verifying or AttemptStatus.Ambiguous or AttemptStatus.Terminal)
            return false;
        if (current.TemporaryFailures >= maxTemporaryFailures) return false;
        return current.RetryAfter is null || current.RetryAfter <= _clock();
    }

    internal AttemptRecord Queue(string accountId, string code, string? correlationId = null)
    {
        var normalized = NormalizeCode(code);
        if (!_state.Attempts.TryGetValue(accountId, out var perAccount))
            _state.Attempts[accountId] = perAccount = new(StringComparer.OrdinalIgnoreCase);
        if (perAccount.TryGetValue(normalized, out var existing) &&
            existing.Status is AttemptStatus.Queued or AttemptStatus.Executing or
                AttemptStatus.Verifying or AttemptStatus.Ambiguous or AttemptStatus.Terminal)
            throw new InvalidOperationException("이미 진행 중이거나 확정 대기 중인 account+coupon 시도가 있습니다.");

        var temporaryFailures = existing?.TemporaryFailures ?? 0;
        var now = _clock();
        return perAccount[normalized] = new AttemptRecord
        {
            AttemptId = Guid.NewGuid().ToString("N"),
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
            Status = AttemptStatus.Queued,
            TemporaryFailures = temporaryFailures,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    internal void MarkExecuting(string accountId, string code) => Transition(accountId, code,
        AttemptStatus.Queued, AttemptStatus.Executing);

    internal void MarkVerifying(string accountId, string code) => Transition(accountId, code,
        AttemptStatus.Executing, AttemptStatus.Verifying);

    internal void MarkTerminal(string accountId, string code, string terminalStatus)
    {
        if (!MainForm.IsCompletedStatus(terminalStatus))
            throw new ArgumentException("확정 결과만 terminal로 기록할 수 있습니다.", nameof(terminalStatus));
        var record = Require(accountId, code);
        if (record.Status is not (AttemptStatus.Executing or AttemptStatus.Verifying))
            throw new InvalidOperationException($"terminal 전이 불가: {record.Status}");
        record.Status = AttemptStatus.Terminal;
        record.TerminalStatus = terminalStatus;
        record.RetryAfter = null;
        record.UpdatedAt = _clock();
    }

    internal void MarkAmbiguous(string accountId, string code)
    {
        var record = Require(accountId, code);
        if (record.Status is not (AttemptStatus.Executing or AttemptStatus.Verifying))
            throw new InvalidOperationException($"ambiguous 전이 불가: {record.Status}");
        record.Status = AttemptStatus.Ambiguous;
        record.RetryAfter = null;
        record.UpdatedAt = _clock();
    }

    internal void MarkTemporaryFailure(string accountId, string code, TimeSpan baseDelay)
    {
        var record = Require(accountId, code);
        if (record.Status is not (AttemptStatus.Queued or AttemptStatus.Executing or AttemptStatus.Verifying))
            throw new InvalidOperationException($"temporary failure 전이 불가: {record.Status}");
        record.TemporaryFailures++;
        var multiplier = Math.Pow(2, Math.Min(record.TemporaryFailures - 1, 6));
        record.Status = AttemptStatus.TemporaryFailure;
        record.UpdatedAt = _clock();
        record.RetryAfter = record.UpdatedAt + TimeSpan.FromTicks((long)(baseDelay.Ticks * multiplier));
    }

    internal static string NormalizeCode(string code)
    {
        var normalized = string.Concat((code ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (normalized.Length is 0 or > 80 || normalized.Contains("://", StringComparison.Ordinal) ||
            normalized.IndexOfAny(['<', '>', '{', '}', '[', ']']) >= 0)
            throw new ArgumentException("쿠폰 코드를 정규화할 수 없습니다.", nameof(code));
        return normalized;
    }

    private void Transition(string accountId, string code, string expected, string next)
    {
        var record = Require(accountId, code);
        if (record.Status != expected)
            throw new InvalidOperationException($"시도 상태 전이 불가: {record.Status} → {next}");
        record.Status = next;
        record.UpdatedAt = _clock();
    }

    private AttemptRecord Require(string accountId, string code) =>
        TryGet(accountId, NormalizeCode(code), out var record)
            ? record
            : throw new KeyNotFoundException("account+coupon 시도 기록이 없습니다.");

    private bool TryGet(string accountId, string normalizedCode, out AttemptRecord record)
    {
        record = null!;
        return _state.Attempts.TryGetValue(accountId, out var perAccount) &&
               perAccount.TryGetValue(normalizedCode, out record!);
    }
}
