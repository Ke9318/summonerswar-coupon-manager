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
        "SUMMONERS", "ACTIVE", "AVAILABLE", "CODES", "COMMUNITY", "PROVIDED",
        "ACCOUNT", "MONSTERS", "DASHBOARD", "PASSWORD", "USERNAME", "DISCORD",
        "REWARDS", "PRIVACY", "CONTACT", "COOKIE", "JAVASCRIPT", "CONTENT",
        "WINDOWS", "MONSTERSGAME", "MONSTERS36"
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
    private static readonly Regex AllowedCode = new(@"^[A-Z0-9]{6,32}$", RegexOptions.Compiled);
    private static readonly Regex HexOnly = new(@"^(?:[0-9A-F]{8}|[0-9A-F]{12}|[0-9A-F]{16}|[0-9A-F]{32})$", RegexOptions.Compiled);

    public CouponSourceService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SWCouponManager/1.1");
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
            "SWQ" => ExtractSwqActiveRows(html),
            _ => []
        };

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
            AddIfPlausible(candidate, result);
        return result.OrderBy(x => x).ToList();
    }

    private static IEnumerable<string> ExtractSwqActiveRows(string html)
    {
        foreach (Match match in TableRow.Matches(html))
        {
            var row = match.Groups["row"].Value;
            var text = DecodeText(row);
            if (Regex.IsMatch(text, @"\b(expired|만료|invalid|무효)\b", RegexOptions.IgnoreCase))
                continue;

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
        if (HexOnly.IsMatch(code)) return;
        if (Guid.TryParse(code, out _)) return;
        output.Add(code);
    }

    private static string DecodeText(string value) =>
        WebUtility.HtmlDecode(StripHtml.Replace(value, " ")).Trim();
}
