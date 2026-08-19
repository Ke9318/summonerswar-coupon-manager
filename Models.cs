using System.Text.Json.Serialization;

namespace SWCouponManager;

public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string HiveId { get; set; } = "";
    public bool Selected { get; set; } = true;
    public string Server { get; set; } = "korea";
}

public sealed class CouponRecord
{
    public string Status { get; set; } = "unknown";
    public string Message { get; set; } = "";
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;
}

public sealed class AppState
{
    public List<Account> Accounts { get; set; } = [];
    public Dictionary<string, Dictionary<string, CouponRecord>> History { get; set; } = [];
    public List<string> LastScanCodes { get; set; } = [];
    public List<string> SeenCodes { get; set; } = [];
    public Dictionary<string, List<string>> CodeSources { get; set; } = [];
    public Dictionary<string, Dictionary<string, ObservedCodeState>> ObservedCodesBySource { get; set; } = [];
    public Dictionary<string, SourceInventoryState> SourceInventories { get; set; } = [];
    public DateTimeOffset? LastScanAt { get; set; }
    public string Mode { get; set; } = "new";
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowW { get; set; } = 720;
    public int WindowH { get; set; } = 640;
}

public sealed class ObservedCodeState
{
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset LastConfirmedAt { get; set; }
    public int ConsecutiveMisses { get; set; }
    public string LastPayloadHash { get; set; } = "";
}

public sealed class SourceInventoryState
{
    public int LastHealthyCount { get; set; }
    public int LastHealthyPayloadBytes { get; set; }
    public DateTimeOffset? LastHealthyAt { get; set; }
}

public sealed record CouponSource(string Name, string Url);
public sealed record ScanResult(
    List<string> Codes,
    Dictionary<string, List<string>> Sources,
    List<string> SuccessfulSources,
    List<string> Errors,
    List<SourceHealth> Health);
public sealed record SourceHealth(
    string Source,
    bool HttpSuccess,
    int PayloadBytes,
    int ProductionCount,
    int? ReferenceCount,
    List<string> MissingCodes,
    List<string> ExtraCodes,
    string? Error,
    int FetchAttempts = 1,
    int FetchSuccesses = 0,
    List<string>? PayloadHashes = null,
    List<int>? ResponseCodeCounts = null,
    int? AdvertisedCount = null,
    int RetainedRecentCount = 0,
    bool Suspicious = false,
    List<string>? Warnings = null);
public sealed record WorkItem(Account Account, string Code);
