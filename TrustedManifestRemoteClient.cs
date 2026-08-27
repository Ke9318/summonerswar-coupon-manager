using System.Net;
using System.Text.Json;

namespace SWCouponManager;

internal sealed class TrustedManifestRemoteClient
{
    internal const string ManifestUrl =
        "https://github.com/Ke9318/summonerswar-coupon-manager/releases/download/candidate-manifest/candidate-manifest.json";
    internal const string ChecksumUrl = ManifestUrl + ".sha256";

    private readonly Func<string, CancellationToken, Task<string>> _fetch;

    internal TrustedManifestRemoteClient(Func<string, CancellationToken, Task<string>>? fetch = null)
    {
        if (fetch is not null)
        {
            _fetch = fetch;
            return;
        }
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        var version = typeof(TrustedManifestRemoteClient).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SWCouponManager/" + version);
        http.DefaultRequestHeaders.CacheControl = new() { NoCache = true, NoStore = true };
        _fetch = async (url, ct) => await http.GetStringAsync(
            url + (url.Contains('?') ? "&" : "?") + "_scm=" + Guid.NewGuid().ToString("N"), ct);
    }

    internal async Task<TrustedCandidateManifest> FetchAsync(
        Version clientVersion, DateTimeOffset now, CancellationToken ct)
    {
        var checksumBefore = await _fetch(ChecksumUrl, ct);
        var json = await _fetch(ManifestUrl, ct);
        var checksumAfter = await _fetch(ChecksumUrl, ct);
        if (!string.Equals(checksumBefore.Trim(), checksumAfter.Trim(), StringComparison.Ordinal))
            throw new IOException("remote manifest 게시가 진행 중이므로 이번 주기를 건너뜁니다.");
        return TrustedCandidateManifestService.ParseAndValidate(json, checksumAfter, clientVersion, now);
    }

    internal static async Task<int> RunGateAsync(string[] args)
    {
        try
        {
            var outputIndex = Array.FindIndex(args, arg => arg.Equals("--output", StringComparison.OrdinalIgnoreCase));
            if (outputIndex < 0 || outputIndex + 1 >= args.Length || !Path.IsPathFullyQualified(args[outputIndex + 1]))
                throw new ArgumentException("--output 절대 경로가 필요합니다.");
            var version = typeof(TrustedManifestRemoteClient).Assembly.GetName().Version ?? new Version(1, 0, 0);
            var manifest = await new TrustedManifestRemoteClient().FetchAsync(version, DateTimeOffset.UtcNow, CancellationToken.None);
            var redacted = JsonSerializer.Serialize(new
            {
                manifest.SchemaVersion,
                manifest.GeneratedAt,
                manifest.ExpiresAt,
                manifest.MinimumClientVersion,
                candidateCount = manifest.Candidates.Count,
                sources = manifest.Candidates.SelectMany(candidate => candidate.Sources)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(source => source).ToList()
            });
            File.WriteAllText(args[outputIndex + 1], redacted);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
