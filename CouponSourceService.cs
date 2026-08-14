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

    private static readonly HashSet<string> UiWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACTIVE", "AVAILABLE", "CODES", "COMMUNITY", "ACCOUNT", "DASHBOARD",
        "PASSWORD", "USERNAME", "REWARDS", "PRIVACY", "CONTACT", "COOKIE",
        "JAVASCRIPT", "CONTENT", "WINDOWS"
    };

    private static readonly Regex SwgtLink = new(
        "<a\\b(?=[^>]*\\bhref\\s*=\\s*['\\\"][^'\\\"]*withhive\\.me[^'\\\"]*['\\\"])[^>]*>(?<code>[^<]+)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CodeTag = new(
        @"<code\b[^>]*>(?<code>[^<]+)</code>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TableRow = new(
        @"<tr\b[^>]*>(?<row>[\s\S]*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TableCell = new(
        @"<td\b[^>]*>(?<cell>[\s\S]*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StripHtml = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex HiddenContent = new(
        @"<!--[\s\S]*?-->|<(script|style)\b[^>]*>[\s\S]*?</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Url = new(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GuidText = new(
        @"\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BroadCandidate = new(
        @"(?<![A-Z0-9])[A-Z0-9]{5,40}(?![A-Z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AllowedCode = new(@"^[A-Z0-9]{5,40}$", RegexOptions.Compiled);
    private static readonly Regex LongHexHash = new(@"^[0-9A-F]{20,}$", RegexOptions.Compiled);

    public CouponSourceService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SWCouponManager/1.2");
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
                var separator = source.Url.Contains('?') ? "&" : "?";
                var html = await _http.GetStringAsync(
                    source.Url + separator + "_=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct);
                var codes = ExtractCodes(source.Name, html);
                if (codes.Count == 0)
                    throw new InvalidOperationException("활성 쿠폰 코드를 찾지 못했습니다.");
                return (source, codes, error: (string?)null);
            }
            catch (Exception ex)
            {
                return (source, codes: new List<string>(), error: ex.Message);
            }
        });

        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
        {
            if (result.error is not null)
            {
                errors.Add($"{result.source.Name}: {result.error}");
                continue;
            }

            foreach (var code in result.codes)
            {
                if (!merged.TryGetValue(code, out var sourceNames))
                    merged[code] = sourceNames = new(StringComparer.OrdinalIgnoreCase);
                sourceNames.Add(result.source.Name);
            }
        }

        var successfulSources = results.Where(r => r.error is null).Select(r => r.source.Name).ToList();
        if (successfulSources.Count == 0)
            throw new InvalidOperationException("모든 쿠폰 소스가 실패했습니다: " + string.Join(" / ", errors));

        return new ScanResult(
            merged.Keys.OrderBy(x => x).ToList(),
            merged.ToDictionary(k => k.Key, v => v.Value.OrderBy(x => x).ToList(),
                                StringComparer.OrdinalIgnoreCase),
            successfulSources,
            errors);
    }

    internal static List<string> ExtractCodes(string sourceName, string html)
    {
        IEnumerable<string> candidates = sourceName switch
        {
            "SWGT" => SwgtLink.Matches(html).Select(m => m.Groups["code"].Value),
            "SW-Teams" => CodeTag.Matches(html).Select(m => m.Groups["code"].Value),
            "SWQ" => ExtractSwqRows(html),
            _ => []
        };

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
            AddIfPlausible(candidate, result);

        // Keep source-specific parsing, then broaden from visible page text.
        // Hive is the final authority for whether a candidate is a real coupon.
        foreach (Match match in BroadCandidate.Matches(GetVisibleText(html)))
            AddIfPlausible(match.Value, result);
        return result.OrderBy(x => x).ToList();
    }

    private static IEnumerable<string> ExtractSwqRows(string html)
    {
        foreach (Match match in TableRow.Matches(html))
        {
            var row = match.Groups["row"].Value;
            var cells = TableCell.Matches(row);
            if (cells.Count > 0)
                yield return cells[0].Groups["cell"].Value;
        }
    }

    private static void AddIfPlausible(string raw, HashSet<string> output)
    {
        var code = DecodeText(raw).Replace(" ", "").Trim().ToUpperInvariant();
        if (!AllowedCode.IsMatch(code)) return;
        if (!code.Any(char.IsLetter)) return;
        if (UiWords.Contains(code)) return;
        if (LongHexHash.IsMatch(code)) return;
        if (Guid.TryParse(code, out _)) return;
        output.Add(code);
    }

    private static string GetVisibleText(string html)
    {
        var visible = HiddenContent.Replace(html, " ");
        visible = GuidText.Replace(visible, " ");
        visible = Url.Replace(visible, " ");
        return DecodeText(visible);
    }

    private static string DecodeText(string value) =>
        WebUtility.HtmlDecode(StripHtml.Replace(value, " ")).Trim();
}
