param(
  [Parameter(Mandatory=$true)][string]$PackageDirectory,
  [Parameter(Mandatory=$true)][string]$PythonExe
)

$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP ('SWCouponManager-P16-Updater-' + [guid]::NewGuid().ToString('N'))
$web = Join-Path $root 'web'
$success = Join-Path $root 'success-install'
$failure = Join-Path $root 'failure-install'
New-Item -ItemType Directory -Force -Path $web,$success,$failure | Out-Null
Copy-Item -Path (Join-Path $PackageDirectory '*') -Destination $success -Recurse -Force
Copy-Item -Path (Join-Path $PackageDirectory '*') -Destination $failure -Recurse -Force
Set-Content -LiteralPath (Join-Path $success 'unrelated.txt') -Value 'preserve-success' -Encoding ascii
Set-Content -LiteralPath (Join-Path $failure 'unrelated.txt') -Value 'preserve-failure' -Encoding ascii
Set-Content -LiteralPath (Join-Path $failure 'trusted_inventory_seed.json') -Value 'synthetic-prior-version-marker' -Encoding ascii

$zip = Join-Path $web 'SWCouponManager-win-x64.zip'
Compress-Archive -Path (Join-Path $PackageDirectory '*') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($zip + '.sha256') -Value $hash -Encoding ascii

$port = 18765
$server = Start-Process -FilePath $PythonExe -ArgumentList @(
  '-m','http.server',$port,'--bind','127.0.0.1','--directory',$web
) -WindowStyle Hidden -PassThru

try {
  for ($i = 0; $i -lt 30; $i++) {
    try {
      $null = Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:$port/SWCouponManager-win-x64.zip.sha256"
      break
    } catch { Start-Sleep -Milliseconds 100 }
  }

  $env:SWCM_UPDATE_TEST_RESTART_ARGS = "--gui-smoke-test --data-dir $(Join-Path $root 'success-state') --gui-smoke-hold-ms 5000"
  $env:SWCM_UPDATE_TEST_LOG_PATH = Join-Path $root 'success-update.log'
  Remove-Item Env:SWCM_UPDATE_TEST_INJECT_POST_COPY_FAILURE -ErrorAction SilentlyContinue
  $client = Start-Process -FilePath (Join-Path $success 'SWCouponManager.exe') -ArgumentList @(
    '--disposable-update-client','--expected-version','1.5.0',
    '--zip-url',"http://127.0.0.1:$port/SWCouponManager-win-x64.zip",
    '--checksum-url',"http://127.0.0.1:$port/SWCouponManager-win-x64.zip.sha256"
  ) -WindowStyle Hidden -Wait -PassThru
  for ($i = 0; $i -lt 200; $i++) {
    if ((Test-Path $env:SWCM_UPDATE_TEST_LOG_PATH) -and
        ((Get-Content $env:SWCM_UPDATE_TEST_LOG_PATH -Raw) -match 'UPDATE_COMPLETE')) { break }
    Start-Sleep -Milliseconds 100
  }
  $successLog = Get-Content $env:SWCM_UPDATE_TEST_LOG_PATH -Raw
  $successOk = $client.ExitCode -eq 0 -and $successLog -match 'UPDATE_COMPLETE' -and
    (Get-Content (Join-Path $success 'unrelated.txt') -Raw) -match 'preserve-success' -and
    (Test-Path (Join-Path $root 'success-state\state.json'))

  $before = @{}
  Get-ChildItem -LiteralPath $failure -File -Recurse | ForEach-Object {
    $before[$_.FullName.Substring($failure.Length)] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
  }
  $env:SWCM_UPDATE_TEST_RESTART_ARGS = "--gui-smoke-test --data-dir $(Join-Path $root 'failure-state') --gui-smoke-hold-ms 1000"
  $env:SWCM_UPDATE_TEST_LOG_PATH = Join-Path $root 'failure-update.log'
  $env:SWCM_UPDATE_TEST_INJECT_POST_COPY_FAILURE = '1'
  $client2 = Start-Process -FilePath (Join-Path $failure 'SWCouponManager.exe') -ArgumentList @(
    '--disposable-update-client','--expected-version','1.5.0',
    '--zip-url',"http://127.0.0.1:$port/SWCouponManager-win-x64.zip",
    '--checksum-url',"http://127.0.0.1:$port/SWCouponManager-win-x64.zip.sha256"
  ) -WindowStyle Hidden -Wait -PassThru
  for ($i = 0; $i -lt 200; $i++) {
    if ((Test-Path $env:SWCM_UPDATE_TEST_LOG_PATH) -and
        ((Get-Content $env:SWCM_UPDATE_TEST_LOG_PATH -Raw) -match 'ROLLBACK_COMPLETE')) { break }
    Start-Sleep -Milliseconds 100
  }
  $failureLog = Get-Content $env:SWCM_UPDATE_TEST_LOG_PATH -Raw
  $after = @{}
  Get-ChildItem -LiteralPath $failure -File -Recurse | ForEach-Object {
    $after[$_.FullName.Substring($failure.Length)] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
  }
  $changed = @($before.Keys | Where-Object { -not $after.ContainsKey($_) -or $before[$_] -ne $after[$_] })
  $rollbackOk = $before.Count -eq $after.Count -and $changed.Count -eq 0
  $restored = $failureLog -match 'ROLLBACK_COMPLETE'
  Write-Output "success=$successOk successClientExit=$($client.ExitCode) rollback=$rollbackOk failureClientExit=$($client2.ExitCode) restoredLog=$restored root=$root"
  if (-not $successOk -or -not $rollbackOk -or -not $restored) { exit 1 }
} finally {
  if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
  Remove-Item Env:SWCM_UPDATE_TEST_RESTART_ARGS -ErrorAction SilentlyContinue
  Remove-Item Env:SWCM_UPDATE_TEST_LOG_PATH -ErrorAction SilentlyContinue
  Remove-Item Env:SWCM_UPDATE_TEST_INJECT_POST_COPY_FAILURE -ErrorAction SilentlyContinue
}
