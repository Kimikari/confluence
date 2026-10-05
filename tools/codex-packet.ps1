<#
.SYNOPSIS
  Have Codex implement a fresh, self-contained packet of catalogue records in its own worktree.

.DESCRIPTION
  Preconditions: docs/notes/<Label>.md is committed on the base branch and has a "Codex task" section
  listing the records (IDs owned by no other open item) and the owned paths.
  Steps: create worktree ..\Confluence-wt\codex-<Label> on branch codex/<Label>; dotnet restore outside the
  sandbox; run `codex exec` (workspace-write) with tools/prompts/codex-packet.md; commit its changes as the
  operator (Codex cannot commit in its sandbox); write out/handoff/<Label>.json in the stage-script REPORT
  shape with branch/worktree/commit/madeWith filled in, ready for a stage script's args.impl.
  Uses the ChatGPT subscription login: OPENAI_API_KEY and CODEX_API_KEY are removed for the child.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/codex-packet.ps1 -Label eqp-t3
#>
param(
  [Parameter(Mandatory = $true)][string]$Label,
  [string]$Base = 'master',
  [string]$Model = 'gpt-6.1-sol',
  [ValidateSet('low', 'medium', 'high', 'xhigh')][string]$Effort = 'medium',
  [switch]$NoNetwork,
  [int]$TimeoutMinutes = 120,
  [switch]$DryRun
)

$ErrorActionPreference = 'Continue'  # native tools write progress to stderr; failures are checked via exit codes
$repo = Split-Path -Parent $PSScriptRoot
$wtRoot = Join-Path (Split-Path -Parent $repo) 'Confluence-wt'
$wt = Join-Path $wtRoot "codex-$Label"
$branch = "codex/$Label"
$handoffDir = Join-Path $repo 'out\handoff'
$handoff = Join-Path $handoffDir "$Label.json"
$log = Join-Path $handoffDir "$Label.log"
$schema = Join-Path $PSScriptRoot 'schemas\report.json'
$template = Join-Path $PSScriptRoot 'prompts\codex-packet.md'
$codex = (Get-Command codex -ErrorAction Stop).Source

function Invoke-Git([string[]]$gitArgs) {
  & git @gitArgs
  if ($LASTEXITCODE -ne 0) { throw "git $($gitArgs -join ' ') failed ($LASTEXITCODE)" }
}
function Join-Args([string[]]$list) {
  ($list | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
}

# The packet description must already be committed on the base branch.
$notes = & git -C $repo show "${Base}:docs/notes/$Label.md" 2>$null
if ($LASTEXITCODE -ne 0 -or -not ($notes -match 'Codex task')) {
  throw "docs/notes/$Label.md with a 'Codex task' section must be committed on $Base first."
}

$argList = @('exec', '-C', $wt, '--sandbox', 'workspace-write')
if (-not $NoNetwork) { $argList += @('-c', 'sandbox_workspace_write.network_access=true') }
$argList += @('--add-dir', $env:TEMP, '-m', $Model, '-c', "model_reasoning_effort=$Effort",
  '--output-schema', $schema, '-o', $handoff, '-')
$argLine = Join-Args $argList

if ($DryRun) {
  Write-Output "worktree: $wt (branch $branch from $Base)"
  Write-Output "codex:    $codex"
  Write-Output "args:     $argLine"
  Write-Output "handoff:  $handoff"
  exit 0
}

New-Item -ItemType Directory -Force $wtRoot, $handoffDir | Out-Null
if (-not (Test-Path $wt)) {
  & git -C $repo show-ref --verify --quiet "refs/heads/$branch"
  if ($LASTEXITCODE -eq 0) { Invoke-Git @('-C', $repo, 'worktree', 'add', $wt, $branch) }
  else { Invoke-Git @('-C', $repo, 'worktree', 'add', $wt, '-b', $branch, $Base) }
}

& dotnet restore (Join-Path $wt 'Confluence.sln')
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

$promptFile = Join-Path $env:TEMP "codex-packet-$Label.md"
(Get-Content -Raw $template).Replace('{LABEL}', $Label) | Set-Content -Path $promptFile -Encoding ascii

foreach ($v in 'OPENAI_API_KEY', 'CODEX_API_KEY') { if (Test-Path "Env:$v") { Remove-Item "Env:$v" } }
Remove-Item $handoff -ErrorAction SilentlyContinue

$p = Start-Process -FilePath $codex -ArgumentList $argLine -WorkingDirectory $wt -NoNewWindow -PassThru `
  -RedirectStandardInput $promptFile -RedirectStandardOutput $log -RedirectStandardError "$log.err"
$null = $p.Handle
$outcome = 'finished'
if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
  & taskkill.exe /PID $p.Id /T /F | Out-Null
  $outcome = "timeout after $TimeoutMinutes min"
} elseif ($p.ExitCode -ne 0) {
  $outcome = "codex exit $($p.ExitCode)"
}

# Operator commit: whatever Codex produced is saved even after a timeout or a quota stop.
Invoke-Git @('-C', $wt, 'add', '-A')
& git -C $wt diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
  Invoke-Git @('-C', $wt, 'commit', '-m', "WIP: codex $Label ($outcome)", '-m', 'Made-with: codex')
}
$commit = (& git -C $wt rev-parse HEAD).Trim()

if (Test-Path $handoff) {
  $r = Get-Content -Raw $handoff | ConvertFrom-Json -ErrorAction Stop
} else {
  $r = [pscustomobject]@{ api = ''; tests = "no report: $outcome"; decisions = @(); contentImplemented = @(); crossEdits = @(); gaps = @("Codex run ended without a report ($outcome); inspect $log") }
}
$r | Add-Member -Force -NotePropertyName branch -NotePropertyValue $branch
$r | Add-Member -Force -NotePropertyName worktree -NotePropertyValue $wt
$r | Add-Member -Force -NotePropertyName commit -NotePropertyValue $commit
$r | Add-Member -Force -NotePropertyName madeWith -NotePropertyValue 'codex'
$r | ConvertTo-Json -Depth 10 | Set-Content -Path $handoff -Encoding utf8

Write-Output "$Label -> $outcome; commit $commit; $(@($r.contentImplemented).Count) records claimed; handoff $handoff"
Write-Output "Next: run tests yourself if Codex could not (dotnet test `"$wt\Confluence.sln`" --filter `"Category!=Slow`"), then pass the handoff JSON as args.impl.$Label to the stage script so a Claude verifier checks it."
