using System.Text.Json;

namespace SWCouponManager;

internal static class CloudWatcher
{
    private static readonly string[] RequiredSources = ["SWGT", "SW-Teams", "SWQ", "GitHub Manual"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static TrustedCandidateManifest BuildManifest(
        ScanResult scan,
        DateTimeOffset now,
        string minimumClientVersion = "1.5.0",
        TimeSpan? lifetime = null)
    {
        var healthBySource = scan.Health.ToDictionary(x => x.Source, StringComparer.OrdinalIgnoreCase);
        foreach (var required in RequiredSources)
        {
            if (!healthBySource.TryGetValue(required, out var health) || !health.HttpSuccess ||
                health.ReferenceCount is null || health.MissingCodes.Count > 0 || health.Suspicious ||
                health.PayloadHashes is null || health.PayloadHashes.Count == 0 ||
                health.PayloadHashes.Any(hash => hash.Length != 64 || !hash.All(Uri.IsHexDigit)))
                throw new InvalidDataException($"필수 소스 publication gate 실패: {required}");
        }
        if (scan.SuccessfulSources.Distinct(StringComparer.OrdinalIgnoreCase).Count() != RequiredSources.Length ||
            RequiredSources.Any(required => !scan.SuccessfulSources.Contains(required, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("필수 소스 성공 집합이 완전하지 않습니다.");

        var candidates = new List<TrustedCandidate>();
        foreach (var code in scan.Codes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!scan.Sources.TryGetValue(code, out var sources) || sources.Count == 0)
                throw new InvalidDataException("출처 없는 candidate는 게시할 수 없습니다.");
            var normalizedSources = sources
                .Where(source => healthBySource.TryGetValue(source, out var health) &&
                    !health.ExtraCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
            if (normalizedSources.Count == 0) continue;
            if (normalizedSources.Any(source => !healthBySource.ContainsKey(source)))
                throw new InvalidDataException("health evidence가 없는 출처가 있습니다.");
            candidates.Add(new TrustedCandidate
            {
                Code = AttemptJournal.NormalizeCode(code),
                Sources = normalizedSources,
                FirstObservedAt = now,
                LastObservedAt = now,
                EvidenceHashes = normalizedSources
                    .SelectMany(source => healthBySource[source].PayloadHashes!)
                    .Select(hash => hash.ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(hash => hash)
                    .ToList(),
                PolicyId = "recall-explicit-v1"
            });
        }

        return new TrustedCandidateManifest
        {
            SchemaVersion = 1,
            GeneratedAt = now,
            ExpiresAt = now + (lifetime ?? TimeSpan.FromHours(6)),
            MinimumClientVersion = minimumClientVersion,
            Candidates = candidates
        };
    }

    internal static string Serialize(TrustedCandidateManifest manifest) =>
        JsonSerializer.Serialize(manifest, JsonOptions);

    internal static void PublishAtomically(
        TrustedCandidateManifest manifest,
        string outputDirectory,
        Action<string, string, bool>? move = null)
    {
        Directory.CreateDirectory(outputDirectory);
        var json = Serialize(manifest);
        var checksum = TrustedCandidateManifestService.ComputeSha256(json);
        _ = TrustedCandidateManifestService.ParseAndValidate(
            json, checksum, Version.Parse(manifest.MinimumClientVersion), manifest.GeneratedAt);

        var manifestPath = Path.Combine(outputDirectory, "candidate-manifest.json");
        var checksumPath = manifestPath + ".sha256";
        var suffix = ".stage-" + Guid.NewGuid().ToString("N");
        var stagedManifest = manifestPath + suffix;
        var stagedChecksum = checksumPath + suffix;
        var backupManifest = manifestPath + ".publish-backup";
        var backupChecksum = checksumPath + ".publish-backup";
        move ??= (source, destination, overwrite) => File.Move(source, destination, overwrite);

        File.WriteAllText(stagedManifest, json);
        File.WriteAllText(stagedChecksum, checksum + Environment.NewLine);
        try
        {
            if (File.Exists(manifestPath)) File.Copy(manifestPath, backupManifest, true);
            if (File.Exists(checksumPath)) File.Copy(checksumPath, backupChecksum, true);
            move(stagedManifest, manifestPath, true);
            move(stagedChecksum, checksumPath, true);
        }
        catch
        {
            if (File.Exists(backupManifest)) File.Copy(backupManifest, manifestPath, true);
            else if (File.Exists(manifestPath)) File.Delete(manifestPath);
            if (File.Exists(backupChecksum)) File.Copy(backupChecksum, checksumPath, true);
            else if (File.Exists(checksumPath)) File.Delete(checksumPath);
            throw;
        }
        finally
        {
            foreach (var path in new[] { stagedManifest, stagedChecksum, backupManifest, backupChecksum })
                try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var outputIndex = Array.FindIndex(args, arg => arg.Equals("--output", StringComparison.OrdinalIgnoreCase));
            if (outputIndex < 0 || outputIndex + 1 >= args.Length)
                throw new ArgumentException("--output <directory>가 필요합니다.");
            var output = Path.GetFullPath(args[outputIndex + 1]);
            var now = DateTimeOffset.UtcNow;
            var scan = await new CouponSourceService().ScanAsync();
            var manifest = BuildManifest(scan, now);
            PublishAtomically(manifest, output);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
