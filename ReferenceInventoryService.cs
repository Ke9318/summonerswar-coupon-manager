using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SWCouponManager;

// This verifier intentionally does not call or share regexes/selectors with the
// production parser. Each source is inventoried through its canonical markup.
internal static class ReferenceInventoryService
{
    internal static void ValidateShape(string source, string payload)
    {
        var recognized = source switch
        {
            "SWGT" => payload.Contains("Active Codes", StringComparison.OrdinalIgnoreCase) &&
                      payload.Contains("gamecodes", StringComparison.OrdinalIgnoreCase),
            "SW-Teams" => payload.Contains("Available Codes", StringComparison.OrdinalIgnoreCase),
            "SWQ" => payload.Contains("id=\"coupons\"", StringComparison.OrdinalIgnoreCase) &&
                     payload.Contains("code-cell", StringComparison.OrdinalIgnoreCase),
            "GitHub Manual" => payload.Contains("\"codes\"", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
        if (!recognized)
            throw new InvalidDataException($"{source} 응답은 수신했지만 기준 쿠폰 구조를 확인할 수 없습니다.");
    }

    internal static List<string> Extract(string source, string payload) => source switch
    {
        "SWGT" => ExtractSwgtLinks(payload),
        "SW-Teams" => ExtractSwTeamsCodeElements(payload),
        "SWQ" => ExtractSwqCodeCells(payload),
        "GitHub Manual" => ExtractManualObjects(payload),
        _ => throw new NotSupportedException($"{source} 기준 목록 추출기가 없습니다.")
    };

    private static List<string> ExtractSwgtLinks(string html)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match anchor in Regex.Matches(html, """<a\s[^>]*href\s*=\s*['"](?<href>[^'"]+)['"][^>]*>""", RegexOptions.IgnoreCase))
        {
            var href = WebUtility.HtmlDecode(anchor.Groups["href"].Value);
            var marker = href.IndexOf("/313/", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) continue;
            AddUrlSegment(href[(marker + 5)..], found);
        }
        return Sorted(found);
    }

    private static List<string> ExtractSwTeamsCodeElements(string html)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match element in Regex.Matches(html, @"<code(?:\s[^>]*)?>(?<text>[\s\S]*?)</code>", RegexOptions.IgnoreCase))
            AddPlain(Regex.Replace(element.Groups["text"].Value, "<[^>]+>", " "), found);
        return Sorted(found);
    }

    private static List<string> ExtractSwqCodeCells(string html)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match cell in Regex.Matches(html, """<td(?=[^>]*class\s*=\s*['"][^'"]*code-cell)[^>]*>(?<text>[\s\S]*?)</td>""", RegexOptions.IgnoreCase))
            AddPlain(Regex.Replace(cell.Groups["text"].Value, "<[^>]+>", " "), found);
        return Sorted(found);
    }

    private static List<string> ExtractManualObjects(string json)
    {
        using var document = JsonDocument.Parse(json);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in document.RootElement.GetProperty("codes").EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : item.GetProperty("code").GetString();
            AddPlain(value ?? "", found);
        }
        return Sorted(found);
    }

    private static void AddUrlSegment(string value, HashSet<string> found)
    {
        var end = value.IndexOfAny(['?', '#', '/', '&', '\'', '"', '<', '>']);
        AddPlain(end < 0 ? value : value[..end], found);
    }

    private static void AddPlain(string value, HashSet<string> found)
    {
        var code = Regex.Replace(WebUtility.HtmlDecode(value), @"\s+", "").Trim().ToUpperInvariant();
        if (code.Length is >= 1 and <= 80 && code.All(char.IsLetterOrDigit))
            found.Add(code);
    }

    private static List<string> Sorted(HashSet<string> found) => found.OrderBy(x => x).ToList();
}
