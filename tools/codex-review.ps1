<#
.SYNOPSIS
  Second-vendor (Codex) review of a worktree branch against master.

.DESCRIPTION
  Runs `codex exec` read-only in the given worktree with tools/prompts/codex-review.md on stdin and the
  strict tools/schemas/findings.json output schema. Writes out/reviews/<Label>.json (findings),
  <Label>.log / <Label>.log.err (Codex output) and, always last, <Label>.status (exit code / timeout).
  Uses the ChatGPT subscription login: OPENAI_API_KEY and CODEX_API_KEY are removed for the child.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/codex-review.ps1 -Worktree .claude\worktrees\wf_ac402310-4a4-4 -Label net-protocol
#>
param(
  [Parameter(Mandatory = $true)][string]$Worktree,
  [Parameter(Mandatory = $true)][string]$Label,
  [string]$Model = 'gpt-6.1-sol',
  [ValidateSet('low', 'medium', 'high', 'xhigh')][string]$Effort = 'high',
  [int]$TimeoutMinutes = 45,
  [switch]$DryRun
)

$ErrorActionPreference = 'Continue'  # native tools write progress to stderr; failures are checked via exit codes
$repo = Split-Path -Parent $PSScriptRoot
$wt = (Resolve-Path $Worktree -ErrorAction Stop).Path
$outDir = Join-Path $repo 'out\reviews'
New-Item -ItemType Directory -Force $outDir | Out-Null
$out = Join-Path $outDir "$Label.json"
$status = Join-Path $outDir "$Label.status"
$log = Join-Path $outDir "$Label.log"
$schema = Join-Path $PSScriptRoot 'schemas\findings.json'
$prompt = Join-Path $PSScriptRoot 'prompts\codex-review.md'
$codex = (Get-Command codex -ErrorAction Stop).Source

function Join-Args([string[]]$list) {
  ($list | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
}

$argList = @('exec', '-s', 'read-only', '-C', $wt, '-m', $Model, '-c', "model_reasoning_effort=$Effort",
  '--output-schema', $schema, '-o', $out, '-')
$argLine = Join-Args $argList

if ($DryRun) {
  Write-Output "codex: $codex"
  Write-Output "args:  $argLine"
  Write-Output "stdin: $prompt"
  Write-Output "out:   $out"
  exit 0
}

Remove-Item $out, $status -ErrorAction SilentlyContinue
foreach ($v in 'OPENAI_API_KEY', 'CODEX_API_KEY') { if (Test-Path "Env:$v") { Remove-Item "Env:$v" } }

$result = 'unknown'
try {
  $p = Start-Process -FilePath $codex -ArgumentList $argLine -WorkingDirectory $wt -NoNewWindow -PassThru `
    -RedirectStandardInput $prompt -RedirectStandardOutput $log -RedirectStandardError "$log.err"
  $null = $p.Handle  # cache the handle so ExitCode is available later
  if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    & taskkill.exe /PID $p.Id /T /F | Out-Null
    $result = "timeout after $TimeoutMinutes min"
  } elseif ($p.ExitCode -ne 0) {
    $result = "codex exit $($p.ExitCode)"
  } elseif (-not (Test-Path $out)) {
    $result = 'codex exit 0 but no output file'
  } else {
    $r = Get-Content -Raw $out | ConvertFrom-Json -ErrorAction Stop
    $result = "ok: $(@($r.findings).Count) findings, $(@($r.checkedIds).Count) ids checked"
  }
} catch {
  $result = "error: $($_.Exception.Message)"
} finally {
  Set-Content -Path $status -Encoding ascii -Value $result
}
Write-Output "$Label -> $result"
if ($result -notlike 'ok:*') { exit 1 }
