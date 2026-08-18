using System.Text.Json;

namespace SWCouponManager;

public sealed class AppStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string DataDir { get; }

    public AppStorage(string? dataDir = null)
    {
        DataDir = dataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SWCouponManager");
    }

    public string StatePath => Path.Combine(DataDir, "state.json");
    public string BackupPath => Path.Combine(DataDir, "state.backup.json");

    public AppState Load()
    {
        Directory.CreateDirectory(DataDir);

        foreach (var path in new[] { StatePath, BackupPath })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var text = File.ReadAllText(path);
                var state = JsonSerializer.Deserialize<AppState>(text, JsonOptions);
                if (state is not null)
                {
                    state.Accounts ??= [];
                    state.History ??= [];
                    state.LastScanCodes ??= [];
                    state.SeenCodes ??= [];
                    state.CodeSources ??= [];
                    if (state.SeenCodes.Count == 0 && state.LastScanCodes.Count > 0)
                        state.SeenCodes = state.LastScanCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    return state;
                }
            }
            catch { }
        }

        return new AppState();
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(DataDir);

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var tmp = StatePath + ".tmp";

        File.WriteAllText(tmp, json);

        if (!File.Exists(StatePath))
        {
            File.Move(tmp, StatePath);
            return;
        }

        try
        {
            // File.Replace는 기존 정상 파일을 백업으로 남기면서 원자적으로 교체한다.
            File.Replace(tmp, StatePath, BackupPath, true);
        }
        catch
        {
            // 손상된 기본 파일이 정상 백업을 덮어쓰지 않도록 검증 후에만 복사한다.
            if (CanDeserialize(StatePath))
                File.Copy(StatePath, BackupPath, true);
            File.Move(tmp, StatePath, true);
        }
    }

    private static bool CanDeserialize(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), JsonOptions) is not null;
        }
        catch
        {
            return false;
        }
    }
}
