using System.Text.Json;

namespace SWCouponManager;

internal static class HistoryAudit
{
    internal static int Run(string[] args)
    {
        try
        {
            var historyPath = GetValue(args, "--audit-history");
            var codesPath = GetValue(args, "--audit-codes");
            if (historyPath is null || codesPath is null)
                throw new ArgumentException("사용법: --audit-history <state.json> --audit-codes <codes.json|txt>");

            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(historyPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new JsonException("History 상태 파일을 읽을 수 없습니다.");
            var known = ReadCodes(codesPath);
            var lines = new List<string>();

            foreach (var account in state.Accounts)
            {
                state.History.TryGetValue(account.Id, out var records);
                records ??= new(StringComparer.OrdinalIgnoreCase);
                var untried = known.Where(item => !records.ContainsKey(item.Code)).OrderBy(item => item.Code).ToList();
                lines.Add($"ACCOUNT {account.Name} ({account.Id}) untried={untried.Count}");
                foreach (var item in untried)
                    lines.Add($"  {(item.IsSwcOrEmblem ? "[SWC/EMBLEM] " : "")}{item.Code}");
            }
            var outputPath = GetValue(args, "--audit-output") ?? Path.ChangeExtension(historyPath, ".audit.txt");
            File.WriteAllLines(outputPath, lines);
            foreach (var line in lines) Console.WriteLine(line);
            Console.WriteLine($"AUDIT_REPORT {Path.GetFullPath(outputPath)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static List<AuditCode> ReadCodes(string path)
    {
        var text = File.ReadAllText(path);
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.GetProperty("codes").EnumerateArray().Select(item =>
            {
                var code = item.ValueKind == JsonValueKind.String
                    ? item.GetString() ?? ""
                    : item.GetProperty("code").GetString() ?? "";
                var category = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("category", out var value)
                    ? value.GetString() ?? ""
                    : "";
                return new AuditCode(code.ToUpperInvariant(), IsSwc(code) ||
                    category.Contains("swc", StringComparison.OrdinalIgnoreCase) ||
                    category.Contains("emblem", StringComparison.OrdinalIgnoreCase));
            }).DistinctBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();
        }
        return text.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => new AuditCode(x.ToUpperInvariant(), IsSwc(x)))
            .DistinctBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? GetValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static bool IsSwc(string code) =>
        code.Contains("SWC", StringComparison.OrdinalIgnoreCase) ||
        code.Contains("TICKET", StringComparison.OrdinalIgnoreCase) ||
        code.Contains("HAMBURG", StringComparison.OrdinalIgnoreCase) ||
        code.Contains("INVO", StringComparison.OrdinalIgnoreCase);

    private sealed record AuditCode(string Code, bool IsSwcOrEmblem);
}
