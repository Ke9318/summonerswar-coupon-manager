using System.Net;

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
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SWCouponManager/1.5.0");
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
}
