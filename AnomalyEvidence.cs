using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SWCouponManager;

internal sealed record RedactedSourceEvidence(
    string Source,
    bool HttpSuccess,
    int ProductionCount,
    int? ReferenceCount,
    int? AdvertisedCount,
    int MissingCount,
    int ExtraCount,
    bool Suspicious,
    List<string> PayloadHashes);

internal sealed record AnomalyEvidence(
    string CorrelationId,
    DateTimeOffset DetectedAt,
    string ClientVersion,
    string ParserVersion,
    string EvidenceGroup,
    List<RedactedSourceEvidence> Sources,
    int CandidateCount,
    int SourceErrorCount);

internal enum RepairEligibility
{
    ReviewOnly,
    Blocked
}

internal sealed record RepairProposal(string Kind, List<string> AffectedAreas, bool HasRegressionFixture);

internal static class AnomalyEvidenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly HashSet<string> ProtectedAreas = new(StringComparer.OrdinalIgnoreCase)
    {
        "account", "hive", "credential", "history", "attempt", "persistence-migration",
        "updater", "permission", "workflow-activation", "release", "installed-app"
    };

    internal static AnomalyEvidence Create(
        ScanResult scan,
        string correlationId,
        DateTimeOffset detectedAt,
        string clientVersion,
        string parserVersion)
    {
        if (string.IsNullOrWhiteSpace(correlationId)) throw new ArgumentException("correlation ID가 필요합니다.");
        var sources = scan.Health.OrderBy(x => x.Source).Select(x => new RedactedSourceEvidence(
            x.Source, x.HttpSuccess, x.ProductionCount, x.ReferenceCount, x.AdvertisedCount,
            x.MissingCodes.Count, x.ExtraCodes.Count, x.Suspicious,
            (x.PayloadHashes ?? []).OrderBy(hash => hash).ToList())).ToList();
        var groupInput = parserVersion + "\n" + string.Join("\n", sources.Select(source =>
            $"{source.Source}|{string.Join(',', source.PayloadHashes)}|{source.ProductionCount}|{source.ReferenceCount}|{source.Suspicious}"));
        var group = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(groupInput))).ToLowerInvariant();
        return new AnomalyEvidence(correlationId, detectedAt, clientVersion, parserVersion,
            group, sources, scan.Codes.Count, scan.Errors.Count);
    }

    internal static RepairEligibility Classify(RepairProposal proposal)
    {
        if (proposal.AffectedAreas.Any(ProtectedAreas.Contains)) return RepairEligibility.Blocked;
        if (!proposal.HasRegressionFixture) return RepairEligibility.Blocked;
        return proposal.Kind is "source-parser" or "source-diagnostic"
            ? RepairEligibility.ReviewOnly
            : RepairEligibility.Blocked;
    }

    internal static string Serialize(AnomalyEvidence evidence)
    {
        var json = JsonSerializer.Serialize(evidence, JsonOptions);
        using var document = JsonDocument.Parse(json);
        RejectPrivateKeys(document.RootElement);
        return json;
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var outputIndex = Array.FindIndex(args, arg => arg.Equals("--output", StringComparison.OrdinalIgnoreCase));
            if (outputIndex < 0 || outputIndex + 1 >= args.Length)
                throw new ArgumentException("--output <file>이 필요합니다.");
            var scan = await new CouponSourceService().ScanAsync();
            var version = typeof(AnomalyEvidenceService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            var evidence = Create(scan, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, version, version);
            var output = Path.GetFullPath(args[outputIndex + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, Serialize(evidence));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void RejectPrivateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var name = property.Name.ToLowerInvariant();
                if (name.Contains("account") || name.Contains("hive") || name.Contains("credential") ||
                    name.Contains("password") || name.Contains("token"))
                    throw new InvalidDataException("anomaly evidence에 private key가 포함되었습니다.");
                RejectPrivateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectPrivateKeys(item);
    }
}
