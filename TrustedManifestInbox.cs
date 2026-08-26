namespace SWCouponManager;

internal static class TrustedManifestInbox
{
    internal static TrustedCandidateManifest? TryRead(
        string manifestPath,
        string checksumPath,
        Version clientVersion,
        DateTimeOffset now,
        Func<string, bool>? exists = null,
        Func<string, string>? readAllText = null)
    {
        exists ??= File.Exists;
        readAllText ??= File.ReadAllText;
        if (!exists(manifestPath) || !exists(checksumPath)) return null;

        var checksumBefore = readAllText(checksumPath);
        var json = readAllText(manifestPath);
        var checksumAfter = readAllText(checksumPath);
        if (!string.Equals(checksumBefore, checksumAfter, StringComparison.Ordinal))
            throw new IOException("candidate manifest를 게시하는 중이므로 이번 주기는 건너뜁니다.");

        return TrustedCandidateManifestService.ParseAndValidate(json, checksumAfter, clientVersion, now);
    }
}
