using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SWCouponManager;

public sealed class CouponSourceService
{
    internal const string RemoteCandidatesUrl =
        "https://raw.githubusercontent.com/Ke9318/summonerswar-coupon-manager/main/coupon_candidates.json";

    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    }) { Timeout = TimeSpan.FromSeconds(15) };

    private readonly CouponSource[] _sources;
    private readonly Func<CouponSource, CancellationToken, Task<string>> _fetch;

    private static readonly CouponSource[] DefaultSources =
    [
        new("SWGT", "https://swgt.io/gamecodes"),
        new("SW-Teams", "https://sw-teams.ovh/codes"),
        new("SWQ", "https://swq.jp/"),
        new("GitHub Manual", RemoteCandidatesUrl)
    ];

    private static readonly HashSet<string> UiWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ABOUT", "ACCESS", "ACTIVELY", "ACTIVE", "AVAILABLE", "CODES",
        "COMMUNITY", "ACCOUNT", "DASHBOARD", "PASSWORD", "USERNAME",
        "REWARDS", "PRIVACY", "CONTACT", "COOKIE", "JAVASCRIPT",
        "CONTENT", "WINDOWS"
    };

    private static readonly Regex HiveLinkCode = new(
        @"withhive\.me/313/(?<code>[A-Z0-9]{1,80})(?:[^A-Z0-9]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CodeTag = new(
        @"<code\b[^>]*>(?<code>[\s\S]*?)</code>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExplicitJsonField = new(
        "[\\\"](?:code|coupon|couponCode|promoCode)[\\\"]\\s*:\\s*[\\\"](?<code>[^\\\"\\r\\n]{1,160})[\\\"]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExplicitCodeAttribute = new(
        "(?:data-(?:coupon-?)?code|data-coupon|coupon(?:-?code)?)\\s*=\\s*['\\\"](?<code>[^'\\\"]{1,160})['\\\"]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SwgtExplicitCodeAttribute = new(
        "(?:data-gamecode|data-clipboard-text)\\s*=\\s*['\\\"](?<code>[^'\\\"]{1,160})['\\\"]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SwgtGameCodeLink = new(
        "<a\\b(?=[^>]*\\bclass\\s*=\\s*['\\\"][^'\\\"]*\\bgameCodeLink\\b[^'\\\"]*['\\\"])[^>]*>(?<code>[\\s\\S]*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Table = new(
        @"<table\b[^>]*>(?<table>[\s\S]*?)</table>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TableRow = new(
        @"<tr\b[^>]*>(?<row>[\s\S]*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TableCell = new(
        @"<t[hd]\b[^>]*>(?<cell>[\s\S]*?)</t[hd]>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HiddenContent = new(
        @"<!--[\s\S]*?-->|<(script|style)\b[^>]*>[\s\S]*?</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StripHtml = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex ContextThenCode = new(
        @"(?:coupon(?:\s*code)?|promo(?:\s*code)?|game\s*code|쿠폰(?:\s*코드)?|프로모션\s*코드)\s*[:#\-]?\s*(?<code>[A-Z0-9]{5,40})(?![A-Z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CodeThenContext = new(
        @"(?<![A-Z0-9])(?<code>[A-Z0-9]{5,40})\s*(?:coupon|promo\s*code|쿠폰|프로모션\s*코드)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AllowedCode = new(@"^[A-Z0-9]{5,40}$", RegexOptions.Compiled);
    private static readonly Regex LongHexHash = new(@"^[0-9A-F]{20,}$", RegexOptions.Compiled);

    public CouponSourceService()
    {
        _sources = DefaultSources;
        _fetch = FetchDefaultAsync;
        var version = typeof(CouponSourceService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SWCouponManager/" + version);
        _http.DefaultRequestHeaders.CacheControl = new() { NoCache = true };
    }

    internal CouponSourceService(
        CouponSource[] sources,
        Func<CouponSource, CancellationToken, Task<string>> fetch)
    {
        _sources = sources;
        _fetch = fetch;
    }

    public async Task<ScanResult> ScanAsync(CancellationToken ct = default)
    {
        var tasks = _sources.Select(async source =>
        {
            string payload;
            try
            {
                payload = await _fetch(source, ct);
            }
            catch (Exception ex)
            {
                return new SourceScan(source.Name, [], new SourceHealth(
                    source.Name, false, 0, 0, null, [], [], "network: " + ex.Message), ex.Message);
            }

            try
            {
                var codes = source.Name == "GitHub Manual"
                    ? ExtractRemoteCandidates(payload)
                    : ExtractCodes(source.Name, payload);
                var supportsReference = source.Name is "SWGT" or "SW-Teams" or "SWQ" or "GitHub Manual";
                if (supportsReference)
                    ReferenceInventoryService.ValidateShape(source.Name, payload);
                var reference = supportsReference ? ReferenceInventoryService.Extract(source.Name, payload) : [];
                var missing = supportsReference
                    ? reference.Except(codes, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList()
                    : [];
                var extra = supportsReference
                    ? codes.Except(reference, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList()
                    : [];
                var health = new SourceHealth(
                    source.Name, true, System.Text.Encoding.UTF8.GetByteCount(payload), codes.Count,
                    supportsReference ? reference.Count : null, missing, extra, null);
                return new SourceScan(source.Name, codes, health, null);
            }
            catch (Exception ex)
            {
                return new SourceScan(source.Name, [], new SourceHealth(
                    source.Name, true, System.Text.Encoding.UTF8.GetByteCount(payload), 0,
                    null, [], [], "parser: " + ex.Message), ex.Message);
            }
        });

        return MergeResults(await Task.WhenAll(tasks));
    }

    private async Task<string> FetchDefaultAsync(CouponSource source, CancellationToken ct)
    {
        var separator = source.Url.Contains('?') ? "&" : "?";
        var url = source.Url + separator + "_=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return await _http.GetStringAsync(url, ct);
    }

    internal static ScanResult MergeResults(IEnumerable<SourceScan> results)
    {
        var all = results.ToList();
        var merged = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var result in all)
        {
            if (result.Error is not null)
            {
                errors.Add($"{result.Source}: {result.Error}");
                continue;
            }

            foreach (var code in result.Codes)
            {
                if (!merged.TryGetValue(code, out var sourceNames))
                    merged[code] = sourceNames = new(StringComparer.OrdinalIgnoreCase);
                sourceNames.Add(result.Source);
            }
        }

        foreach (var result in all.Where(x => x.Health.MissingCodes.Count > 0))
            errors.Add($"{result.Source}: parser regression; missing [{string.Join(", ", result.Health.MissingCodes)}]");

        var successfulSources = all.Where(r => r.Error is null).Select(r => r.Source).ToList();
        if (successfulSources.Count == 0)
            throw new InvalidOperationException("모든 쿠폰 소스가 실패했습니다: " + string.Join(" / ", errors));

        return new ScanResult(
            merged.Keys.OrderBy(x => x).ToList(),
            merged.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.OrderBy(x => x).ToList(),
                StringComparer.OrdinalIgnoreCase),
            successfulSources,
            errors,
            all.Select(x => x.Health).ToList());
    }

    internal static List<string> ExtractCodes(string sourceName, string html)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in HiveLinkCode.Matches(html))
            AddExplicit(match.Groups["code"].Value, result);
        foreach (Match match in CodeTag.Matches(html))
            AddExplicit(match.Groups["code"].Value, result);
        foreach (Match match in ExplicitJsonField.Matches(html))
            AddExplicit(match.Groups["code"].Value, result);
        foreach (Match match in ExplicitCodeAttribute.Matches(html))
            AddExplicit(match.Groups["code"].Value, result);
        if (sourceName.Equals("SWGT", StringComparison.OrdinalIgnoreCase))
        {
            // SWGT currently repeats every Active Code in its Hive URL, data-gamecode,
            // clipboard value and link text. Preserve all explicit representations so
            // a harmless markup change in any one of them cannot silently empty the feed.
            foreach (Match match in SwgtExplicitCodeAttribute.Matches(html))
                AddExplicit(match.Groups["code"].Value, result);
            foreach (Match match in SwgtGameCodeLink.Matches(html))
                AddExplicit(match.Groups["code"].Value, result);
        }
        foreach (var candidate in ExtractCouponTableCodes(sourceName, html))
            AddExplicit(candidate, result);

        var visibleText = DecodeText(HiddenContent.Replace(html, " "));
        foreach (Match match in ContextThenCode.Matches(visibleText))
            AddContextCandidate(match.Groups["code"].Value, result);
        foreach (Match match in CodeThenContext.Matches(visibleText))
            AddContextCandidate(match.Groups["code"].Value, result);

        return result.OrderBy(x => x).ToList();
    }

    internal static List<string> ExtractRemoteCandidates(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("codes", out var codes) ||
            codes.ValueKind != JsonValueKind.Array)
            throw new JsonException("codes 배열이 없습니다.");

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in codes.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
                AddExplicit(entry.GetString() ?? "", result);
            else if (entry.ValueKind == JsonValueKind.Object &&
                     entry.TryGetProperty("code", out var code))
                AddExplicit(code.ValueKind == JsonValueKind.String
                    ? code.GetString() ?? ""
                    : code.ToString(), result);
        }
        return result.OrderBy(x => x).ToList();
    }

    private static IEnumerable<string> ExtractCouponTableCodes(string sourceName, string html)
    {
        foreach (Match tableMatch in Table.Matches(html))
        {
            var rows = TableRow.Matches(tableMatch.Groups["table"].Value)
                .Select(match => TableCell.Matches(match.Groups["row"].Value)
                    .Select(cell => DecodeText(cell.Groups["cell"].Value)).ToList())
                .Where(cells => cells.Count > 0)
                .ToList();
            if (rows.Count == 0) continue;

            var codeColumn = rows[0].FindIndex(cell =>
                Regex.IsMatch(cell, @"\b(code|coupon)\b|쿠폰", RegexOptions.IgnoreCase));
            var startRow = codeColumn >= 0 ? 1 : 0;
            if (codeColumn < 0 && sourceName == "SWQ") codeColumn = 0;
            if (codeColumn < 0) continue;

            for (var rowIndex = startRow; rowIndex < rows.Count; rowIndex++)
                if (codeColumn < rows[rowIndex].Count)
                    yield return rows[rowIndex][codeColumn];
        }
    }

    private static void AddExplicit(string raw, HashSet<string> output)
    {
        var decoded = DecodeText(raw).Trim();
        if (decoded.Length == 0 || decoded.Length > 80) return;
        if (decoded.Contains("://", StringComparison.OrdinalIgnoreCase) ||
            decoded.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return;

        var code = Regex.Replace(decoded, @"\s+", "").ToUpperInvariant();
        if (code.Length == 0 || code.Length > 80) return;
        if (code.IndexOfAny(['<', '>', '{', '}', '[', ']']) >= 0) return;
        output.Add(code);
    }

    private static void AddContextCandidate(string raw, HashSet<string> output)
    {
        var code = DecodeText(raw).Replace(" ", "").Trim().ToUpperInvariant();
        if (!AllowedCode.IsMatch(code)) return;
        if (!code.Any(char.IsLetter)) return;
        if (UiWords.Contains(code)) return;
        if (LongHexHash.IsMatch(code)) return;
        if (Guid.TryParse(code, out _)) return;
        output.Add(code);
    }

    private static string DecodeText(string value) =>
        WebUtility.HtmlDecode(StripHtml.Replace(value, " ")).Trim();

    internal sealed record SourceScan(string Source, List<string> Codes, SourceHealth Health, string? Error)
    {
        internal SourceScan(string source, List<string> codes, string? error)
            : this(source, codes, new SourceHealth(source, error is null, 0, codes.Count,
                null, [], [], error), error) { }
    }
}
