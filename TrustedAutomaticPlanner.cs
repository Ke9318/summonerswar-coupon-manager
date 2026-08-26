namespace SWCouponManager;

internal sealed record AutomaticWorkItem(Account Account, TrustedCandidate Candidate)
{
    internal WorkItem Redemption => new(Account, Candidate.Code);
}

internal sealed record AutomaticWorkPlan(
    List<AutomaticWorkItem> Items,
    int SkippedAccounts,
    int SkippedCandidates);

internal sealed class TrustedAutomaticPlanner
{
    private readonly AppState _state;
    private readonly AttemptJournal _journal;

    internal TrustedAutomaticPlanner(AppState state, Func<DateTimeOffset>? clock = null)
    {
        _state = state;
        _journal = new AttemptJournal(state, clock);
    }

    internal AutomaticWorkPlan Build(TrustedCandidateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var duplicateIds = _state.Accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.Id))
            .GroupBy(account => account.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var eligibleAccounts = _state.Accounts.Where(account =>
            account.Selected &&
            !string.IsNullOrWhiteSpace(account.Id) &&
            !string.IsNullOrWhiteSpace(account.HiveId) &&
            MainForm.IsSupportedServer(account.Server) &&
            !duplicateIds.Contains(account.Id)).ToList();
        var skippedAccounts = _state.Accounts.Count - eligibleAccounts.Count;

        var items = new List<AutomaticWorkItem>();
        var skippedCandidates = 0;
        foreach (var candidate in manifest.Candidates)
        {
            var normalized = AttemptJournal.NormalizeCode(candidate.Code);
            if (!string.Equals(normalized, candidate.Code, StringComparison.Ordinal) ||
                candidate.Sources.Count == 0 || candidate.EvidenceHashes.Count == 0)
                throw new InvalidDataException("검증되지 않은 manifest candidate가 planner에 전달되었습니다.");

            var added = 0;
            foreach (var account in eligibleAccounts)
            {
                if (!_journal.CanQueue(account.Id, normalized, _state.History)) continue;
                items.Add(new AutomaticWorkItem(account, candidate));
                added++;
            }
            if (added == 0) skippedCandidates++;
        }

        return new AutomaticWorkPlan(items, skippedAccounts, skippedCandidates);
    }
}
