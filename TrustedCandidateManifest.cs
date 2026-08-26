using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SWCouponManager;

public sealed class TrustedCandidateManifest
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string MinimumClientVersion { get; set; } = "1.0.0";
    public List<TrustedCandidate> Candidates { get; set; } = [];
}

public sealed class TrustedCandidate
{
    public string Code { get; set; } = "";
    public List<string> Sources { get; set; } = [];
    public DateTimeOffset FirstObservedAt { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }
    public List<string> EvidenceHashes { get; set; } = [];
    public string PolicyId { get; set; } = "recall-explicit-v1";
}

internal static class TrustedCandidateManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static TrustedCandidateManifest ParseAndValidate(
        string json, string expectedSha256, Version clientVersion, DateTimeOffset now)
    {
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var expected = expectedSha256.Trim().Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit) ||
            !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("candidate manifest SHA-256 검증에 실패했습니다.");

        using (var raw = JsonDocument.Parse(json)) RejectPrivateFields(raw.RootElement);
        var manifest = JsonSerializer.Deserialize<TrustedCandidateManifest>(json, JsonOptions)
            ?? throw new JsonException("candidate manifest를 읽을 수 없습니다.");
        if (manifest.SchemaVersion != 1) throw new InvalidDataException("지원하지 않는 manifest schema입니다.");
        if (!Version.TryParse(manifest.MinimumClientVersion, out var minimum) || clientVersion < minimum)
            throw new InvalidDataException("manifest가 더 최신 클라이언트를 요구합니다.");
        if (manifest.GeneratedAt > now.AddMinutes(10) || manifest.ExpiresAt <= now || manifest.ExpiresAt <= manifest.GeneratedAt)
            throw new InvalidDataException("manifest 생성/만료 시간이 유효하지 않습니다.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in manifest.Candidates)
        {
            candidate.Code = AttemptJournal.NormalizeCode(candidate.Code);
            candidate.Sources = candidate.Sources.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            candidate.EvidenceHashes = candidate.EvidenceHashes.Where(IsSha256)
                .Select(x => x.ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            if (!seen.Add(candidate.Code)) throw new InvalidDataException("manifest에 중복 coupon identity가 있습니다.");
            if (candidate.Sources.Count == 0 || candidate.EvidenceHashes.Count == 0)
                throw new InvalidDataException("candidate attribution/evidence가 비어 있습니다.");
            if (candidate.FirstObservedAt > candidate.LastObservedAt || candidate.LastObservedAt > now.AddMinutes(10))
                throw new InvalidDataException("candidate 관측 시간이 유효하지 않습니다.");
        }
        return manifest;
    }

    internal static string ComputeSha256(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static void RejectPrivateFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var name = property.Name.ToLowerInvariant();
                if (name.Contains("account") || name.Contains("hive") || name.Contains("credential") ||
                    name.Contains("password") || name.Contains("token"))
                    throw new InvalidDataException($"cloud manifest 금지 필드: {property.Name}");
                RejectPrivateFields(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectPrivateFields(item);
    }
}
