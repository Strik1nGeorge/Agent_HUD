param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskNode = Get-Command node -ErrorAction SilentlyContinue
if ($taskNode) { $taskNodePath = $taskNode.Source } else {
  $taskNodePath = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
}
if (-not (Test-Path -LiteralPath $taskNodePath)) { throw 'Node.js 22 or newer is required.' }
$taskPort = if ($env:PORT) { $env:PORT } else { '4318' }
$taskUrl = "http://127.0.0.1:$taskPort"
$taskHealthy = $false
try { $taskHealthy = (Invoke-RestMethod -Uri "$taskUrl/api/health" -TimeoutSec 2).ok } catch {}
if (-not $taskHealthy) {
  $taskData = Join-Path $taskRoot '.data'
  New-Item -ItemType Directory -Force -Path $taskData | Out-Null
  $taskProcess = Start-Process -FilePath $taskNodePath -ArgumentList @(('"' + (Join-Path $taskRoot 'server.mjs') + '"')) -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskData 'server.log') -RedirectStandardError (Join-Path $taskData 'server-error.log')
  Set-Content -LiteralPath (Join-Path $taskData 'server.pid') -Value $taskProcess.Id
  for ($taskAttempt = 0; $taskAttempt -lt 30; $taskAttempt++) {
    Start-Sleep -Milliseconds 300
    try { $taskHealthy = (Invoke-RestMethod -Uri "$taskUrl/api/health" -TimeoutSec 1).ok; if ($taskHealthy) { break } } catch {}
  }
}
if ($taskHealthy) { if (-not $NoBrowser) { Start-Process $taskUrl }; Write-Host "Agent Meter: $taskUrl" }
else { throw 'Startup failed. Check .data/server-error.log.' }
