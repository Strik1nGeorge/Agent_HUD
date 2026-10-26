$ErrorActionPreference = 'Stop'
$taskPidFile = Join-Path $PSScriptRoot '.data\server.pid'
if (-not (Test-Path -LiteralPath $taskPidFile)) { Write-Host 'No server started by the launcher was found.'; exit }
$taskServerId = [int](Get-Content -LiteralPath $taskPidFile)
$taskServer = Get-CimInstance Win32_Process -Filter "ProcessId = $taskServerId"
$taskExpected = Join-Path $PSScriptRoot 'server.mjs'
if ($taskServer -and $taskServer.Name -eq 'node.exe' -and $taskServer.CommandLine.Contains($taskExpected)) {
  $taskChildren = Get-CimInstance Win32_Process -Filter "ParentProcessId = $taskServerId"
  foreach ($taskChild in $taskChildren) { if ($taskChild.Name -eq 'codex.exe' -and $taskChild.CommandLine.Contains('app-server')) { Stop-Process -Id $taskChild.ProcessId -ErrorAction SilentlyContinue } }
  Stop-Process -Id $taskServerId
  Write-Host 'Agent Meter stopped.'
} else { Write-Host 'No matching Agent Meter process; other processes were left alone.' }
Remove-Item -LiteralPath $taskPidFile
