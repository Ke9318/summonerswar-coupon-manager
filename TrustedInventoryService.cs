using System.Text.Json;

namespace SWCouponManager;

internal sealed record TrustedInventorySeed(
    string Source,
    List<string> Codes,
    DateTimeOffset ObservedAt,
    int TtlHours);

internal static class TrustedInventoryService
{
    internal const string FileName = "trusted_inventory_seed.json";
    internal const string RemoteUrl =
        "https://raw.githubusercontent.com/Ke9318/summonerswar-coupon-manager/main/trusted_inventory_seed.json";

    internal static IReadOnlyDictionary<string, TrustedInventorySeed> Load(string? json, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, TrustedInventorySeed>();
        using var document = JsonDocument.Parse(json);
        var seeds = new Dictionary<string, TrustedInventorySeed>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in document.RootElement.GetProperty("sources").EnumerateArray())
        {
            var source = item.GetProperty("source").GetString() ?? "";
            var observedAt = item.GetProperty("observedAt").GetDateTimeOffset();
            var ttlHours = item.GetProperty("ttlHours").GetInt32();
            var codes = item.GetProperty("codes").EnumerateArray()
                .Select(x => x.GetString()?.Trim().ToUpperInvariant() ?? "")
                .Where(x => x.Length > 0 && x.All(char.IsLetterOrDigit))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            if (source.Length > 0 && ttlHours > 0 && observedAt <= now && now - observedAt <= TimeSpan.FromHours(ttlHours))
                seeds[source] = new(source, codes, observedAt, ttlHours);
        }
        return seeds;
    }

    internal static async Task<string?> LoadDefaultJsonAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                RemoteUrl + "?_seed=" + Guid.NewGuid().ToString("N"));
            request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            var path = Path.Combine(AppContext.BaseDirectory, FileName);
            return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
        }
    }
}
