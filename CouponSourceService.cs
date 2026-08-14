using System.Net;
using System.Text.RegularExpressions;

namespace SWCouponManager;

public sealed class CouponSourceService
{
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly CouponSource[] Sources =
    [
        new("SWGT", "https://swgt.io/gamecodes"),
        new("SW-Teams", "https://sw-teams.ovh/codes"),
        new("SWQ", "https://swq.jp/")
    ];

    private static readonly HashSet<string> Blacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUMMONERS","ACTIVE","AVAILABLE","CODES","COMMUNITY","PROVIDED","ACCOUNT",
        "MONSTERS","DASHBOARD","PASSWORD","USERNAME","DISCORD","REWARDS","PRIVACY",
        "CONTACT","COOKIE","JAVASCRIPT","CONTENT","WINDOWS"
    };

    private static readonly Regex Labelled = new(@"\bCODE\s*:?\s*([A-Z0-9]{7,32})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex General = new(@"\b[A-Z0-9]{7,32}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StripHtml = new(@"<[^>]+>", RegexOptions.Compiled);

    public CouponSourceService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SWCouponManager/1.0");
        _http.DefaultRequestHeaders.CacheControl = new() { NoCache = true };
    }

    public async Task<ScanResult> ScanAsync(CancellationToken ct = default)
    {
        var merged = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        var tasks = Sources.Select(async source =>
        {
            try
            {
                var html = await _http.GetStringAsync(source.Url + (source.Url.Contains('?') ? "&" : "?") +
                                                      "_=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct);
                var codes = ExtractCodes(html);
                if (codes.Count == 0)
                    throw new InvalidOperationException("쿠폰 코드를 찾지 못했습니다.");
                return (source, codes, error: (string?)null);
            }
            catch (Exception ex)
            {
                return (source, codes: new List<string>(), error: ex.Message);
            }
        });

        var results = await Task.WhenAll(tasks);

        foreach (var r in results)
        {
            if (r.error is not null)
            {
                errors.Add($"{r.source.Name}: {r.error}");
                continue;
            }

            foreach (var code in r.codes)
            {
                if (!merged.TryGetValue(code, out var set))
                    merged[code] = set = new(StringComparer.OrdinalIgnoreCase);
                set.Add(r.source.Name);
            }
        }

        var successfulSources = results
            .Where(r => r.error is null)
            .Select(r => r.source.Name)
            .ToList();

        if (successfulSources.Count == 0)
            throw new InvalidOperationException("모든 쿠폰 소스가 실패했습니다: " + string.Join(" / ", errors));

        return new ScanResult(
            merged.Keys.OrderBy(x => x).ToList(),
            merged.ToDictionary(k => k.Key, v => v.Value.OrderBy(x => x).ToList(),
                                StringComparer.OrdinalIgnoreCase),
            successfulSources,
            errors
        );
    }

    private static List<string> ExtractCodes(string html)
    {
        var decoded = WebUtility.HtmlDecode(StripHtml.Replace(html, " "));
        var upper = decoded.ToUpperInvariant();

        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Labelled.Matches(upper))
            AddIfPlausible(m.Groups[1].Value, all);

        foreach (Match m in General.Matches(upper))
            AddIfPlausible(m.Value, all);

        return all.ToList();
    }

    private static void AddIfPlausible(string raw, HashSet<string> output)
    {
        var code = raw.Trim().ToUpperInvariant();
        if (Blacklist.Contains(code)) return;
        if (code.Length < 7 || code.Length > 32) return;
        if (!code.Any(char.IsLetter) || !code.Any(char.IsDigit)) return;
        output.Add(code);
    }
}
