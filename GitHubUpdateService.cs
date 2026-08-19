using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SWCouponManager;

public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl);

public sealed class GitHubUpdateService
{
    private const string Repo = "Ke9318/summonerswar-coupon-manager";
    private const string AssetName = "SWCouponManager-win-x64.zip";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public GitHubUpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SWCouponManager/1.3.3");
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public Version CurrentVersion =>
        typeof(GitHubUpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var stream = await _http.GetStreamAsync(
            $"https://api.github.com/repos/{Repo}/releases/latest", ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var clean = tag.Trim().TrimStart('v', 'V');

        if (!Version.TryParse(clean, out var latest))
            return null;

        if (latest <= CurrentVersion)
            return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), AssetName,
                               StringComparison.OrdinalIgnoreCase))
                continue;

            var url = asset.GetProperty("browser_download_url").GetString();
            if (!string.IsNullOrWhiteSpace(url))
                return new UpdateInfo(latest, tag, url);
        }

        return null;
    }

    public async Task DownloadAndRestartAsync(UpdateInfo update,
                                              Action<string>? progress = null,
                                              CancellationToken ct = default)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "SWCouponManagerUpdate",
            update.Version + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var zipPath = Path.Combine(tempRoot, "update.zip");
        progress?.Invoke("새 버전 다운로드 중...");

        await using (var input = await _http.GetStreamAsync(update.DownloadUrl, ct))
        await using (var output = File.Create(zipPath))
        {
            await input.CopyToAsync(output, ct);
        }

        var stagingDir = Path.Combine(tempRoot, "staging");
        ZipFile.ExtractToDirectory(zipPath, stagingDir, true);

        var stagedExe = Path.Combine(stagingDir, "SWCouponManager.exe");
        if (!File.Exists(stagedExe))
            throw new InvalidDataException("업데이트 ZIP에 SWCouponManager.exe가 없습니다.");

        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var exePath = Environment.ProcessPath ??
                      Path.Combine(appDir, "SWCouponManager.exe");
        var pid = Environment.ProcessId;
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SWCouponManager",
            "update.log");

        // 종료 후 교체가 가능한 위치인지 미리 확인한다.
        var writeProbe = Path.Combine(appDir, ".update-write-test");
        await File.WriteAllTextAsync(writeProbe, "ok", ct);
        File.Delete(writeProbe);

        var script = Path.Combine(tempRoot, "apply-update.ps1");
        var ps = $$"""
        $ErrorActionPreference = 'Stop'
        $pidToWait = {{pid}}
        $source = '{{EscapePs(stagingDir)}}'
        $dest = '{{EscapePs(appDir)}}'
        $exe = '{{EscapePs(exePath)}}'
        $log = '{{EscapePs(logPath)}}'

        function Write-UpdateLog([string]$message) {
          Add-Content -LiteralPath $log -Value "[$([DateTimeOffset]::Now.ToString('o'))] $message" -Encoding UTF8
        }

        try {
          Write-UpdateLog '업데이트 적용 시작'

          # Windows PowerShell 5.1에서도 동작하도록 Wait-Process -Timeout을 사용하지 않는다.
          for ($wait = 0; $wait -lt 60; $wait++) {
            if (-not (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)) { break }
            Start-Sleep -Milliseconds 500
          }
          if (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {
            throw '기존 프로그램이 30초 안에 종료되지 않았습니다.'
          }

          $lastError = $null
          for ($attempt = 1; $attempt -le 10; $attempt++) {
            try {
              Get-ChildItem -LiteralPath $source | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination $dest -Recurse -Force
              }
              $lastError = $null
              break
            } catch {
              $lastError = $_
              Write-UpdateLog "파일 교체 재시도 $attempt : $($_.Exception.Message)"
              Start-Sleep -Seconds 1
            }
          }
          if ($null -ne $lastError) { throw $lastError }
          if (-not (Test-Path -LiteralPath $exe)) { throw "실행 파일이 없습니다: $exe" }

          $started = Start-Process -FilePath $exe -WorkingDirectory $dest -PassThru
          Start-Sleep -Milliseconds 800
          if ($started.HasExited) {
            throw "새 프로그램이 즉시 종료되었습니다. 종료 코드: $($started.ExitCode)"
          }
          Write-UpdateLog "업데이트 완료, 새 프로세스 ID: $($started.Id)"
        } catch {
          Write-UpdateLog "업데이트 실패: $($_ | Out-String)"
        }
        """;
        // Windows PowerShell 5.1이 한글 설치 경로를 정확히 읽도록 BOM을 포함한다.
        await File.WriteAllTextAsync(script, ps, new UTF8Encoding(true), ct);

        progress?.Invoke("업데이트 적용을 위해 자동 재시작합니다...");

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        Application.Exit();
    }

    private static string EscapePs(string value) => value.Replace("'", "''");
}
