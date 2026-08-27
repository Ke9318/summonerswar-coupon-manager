using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SWCouponManager;

public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, string ChecksumUrl);

public sealed class GitHubUpdateService
{
    private const string Repo = "Ke9318/summonerswar-coupon-manager";
    private const string AssetName = "SWCouponManager-win-x64.zip";
    private const string ChecksumAssetName = AssetName + ".sha256";
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

        string? downloadUrl = null;
        string? checksumUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrWhiteSpace(url)) continue;
            if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
                downloadUrl = url;
            else if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
                checksumUrl = url;
        }

        // A newer release without integrity metadata is not a safe automatic
        // update and must not be mislabeled as "already latest" by the UI.
        if (downloadUrl is null || checksumUrl is null)
            throw new InvalidDataException("최신 릴리스에 ZIP 또는 SHA-256 체크섬이 없습니다.");
        return new UpdateInfo(latest, tag, downloadUrl, checksumUrl);
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

        progress?.Invoke("다운로드 무결성 확인 중...");
        var checksumText = await _http.GetStringAsync(update.ChecksumUrl, ct);
        VerifySha256(zipPath, checksumText);

        var stagingDir = Path.Combine(tempRoot, "staging");
        ZipFile.ExtractToDirectory(zipPath, stagingDir, true);

        UpdateHealthCheck.ValidateInstallLayout(stagingDir, update.Version);

        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var exePath = Environment.ProcessPath ??
                      Path.Combine(appDir, "SWCouponManager.exe");
        var pid = Environment.ProcessId;
        var logPath = Environment.GetEnvironmentVariable("SWCM_UPDATE_TEST_LOG_PATH") ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SWCouponManager",
                "update.log");

        // 종료 후 교체가 가능한 위치인지 미리 확인한다.
        var writeProbe = Path.Combine(appDir, ".update-write-test");
        await File.WriteAllTextAsync(writeProbe, "ok", ct);
        File.Delete(writeProbe);

        var script = Path.Combine(tempRoot, "apply-update.ps1");
        var testRestartArgs = Environment.GetEnvironmentVariable("SWCM_UPDATE_TEST_RESTART_ARGS") ?? "";
        var injectPostCopyFailure = string.Equals(
            Environment.GetEnvironmentVariable("SWCM_UPDATE_TEST_INJECT_POST_COPY_FAILURE"),
            "1", StringComparison.Ordinal);
        var ps = $$"""
        $ErrorActionPreference = 'Stop'
        $pidToWait = {{pid}}
        $source = '{{EscapePs(stagingDir)}}'
        $dest = '{{EscapePs(appDir)}}'
        $exe = '{{EscapePs(exePath)}}'
        $log = '{{EscapePs(logPath)}}'
        $backup = Join-Path '{{EscapePs(tempRoot)}}' 'backup'
        $expectedVersion = '{{EscapePs(update.Version.ToString(3))}}'
        $restartArgs = '{{EscapePs(testRestartArgs)}}'
        $injectPostCopyFailure = ${{{injectPostCopyFailure.ToString().ToLowerInvariant()}}}
        $backupReady = $false

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

          New-Item -ItemType Directory -Force -Path $backup | Out-Null
          $sourceFiles = @(Get-ChildItem -LiteralPath $source -Recurse -File)
          foreach ($item in $sourceFiles) {
            $relative = $item.FullName.Substring($source.Length).TrimStart('\')
            $current = Join-Path $dest $relative
            if (Test-Path -LiteralPath $current -PathType Leaf) {
              $saved = Join-Path $backup $relative
              New-Item -ItemType Directory -Force -Path (Split-Path -Parent $saved) | Out-Null
              Copy-Item -LiteralPath $current -Destination $saved -Force
            }
          }
          $backupReady = $true
          Write-UpdateLog "교체 대상 $($sourceFiles.Count)개 백업 완료"

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
          if ($injectPostCopyFailure) { throw 'synthetic post-copy failure' }
          if (-not (Test-Path -LiteralPath $exe)) { throw "실행 파일이 없습니다: $exe" }

          $health = Start-Process -FilePath $exe -WorkingDirectory $dest -ArgumentList '--update-health-check', '--expected-version', $expectedVersion -Wait -PassThru
          if ($health.ExitCode -ne 0) {
            throw "새 버전 상태 검사 실패. 종료 코드: $($health.ExitCode)"
          }

          if ([string]::IsNullOrWhiteSpace($restartArgs)) {
            $started = Start-Process -FilePath $exe -WorkingDirectory $dest -PassThru
          } else {
            $started = Start-Process -FilePath $exe -WorkingDirectory $dest -ArgumentList $restartArgs -PassThru
          }
          Start-Sleep -Seconds 2
          if ($started.HasExited) {
            throw "새 프로그램이 즉시 종료되었습니다. 종료 코드: $($started.ExitCode)"
          }
          Write-UpdateLog "업데이트 완료, 새 프로세스 ID: $($started.Id)"
          Write-UpdateLog "UPDATE_COMPLETE processId=$($started.Id)"
        } catch {
          Write-UpdateLog "업데이트 실패: $($_ | Out-String)"
          try {
            if ($backupReady -and (Test-Path -LiteralPath $backup)) {
              $sourceFiles = @(Get-ChildItem -LiteralPath $source -Recurse -File)
              foreach ($item in $sourceFiles) {
                $relative = $item.FullName.Substring($source.Length).TrimStart('\')
                $current = Join-Path $dest $relative
                $saved = Join-Path $backup $relative
                if (Test-Path -LiteralPath $saved -PathType Leaf) {
                  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $current) | Out-Null
                  Copy-Item -LiteralPath $saved -Destination $current -Force
                } elseif (Test-Path -LiteralPath $current -PathType Leaf) {
                  Remove-Item -LiteralPath $current -Force
                }
              }
              Write-UpdateLog '기존 설치 파일 복원 완료'
              Write-UpdateLog 'ROLLBACK_COMPLETE'
              if (Test-Path -LiteralPath $exe) {
                if ([string]::IsNullOrWhiteSpace($restartArgs)) {
                  Start-Process -FilePath $exe -WorkingDirectory $dest | Out-Null
                } else {
                  Start-Process -FilePath $exe -WorkingDirectory $dest -ArgumentList $restartArgs | Out-Null
                }
              }
            }
          } catch {
            Write-UpdateLog "자동 복원 실패: $($_ | Out-String)"
          }
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

    internal static void VerifySha256(string path, string checksumText)
    {
        var expected = checksumText.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new InvalidDataException("업데이트 체크섬 형식이 올바르지 않습니다.");

        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("업데이트 파일의 SHA-256 체크섬이 일치하지 않습니다.");
    }
}
