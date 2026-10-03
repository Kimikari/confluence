<#
.SYNOPSIS
  Builds the Unity client: sync the core libraries, optionally configure the project and run the EditMode
  tests, build the development Windows player, and optionally smoke-test it.

.DESCRIPTION
  1. tools/sync-unity.ps1 (always, unless -SkipSync): Release-builds the netstandard2.1 libraries and copies
     the DLLs and the catalogue into the project (the copies are gitignored, see docs/notes/client.md).
  2. -Configure: runs ProjectConfigurator.Configure in batch mode first (URP assets, input handling, player
     settings, bootstrap scene). Only needed after changing the configurator; its output is committed.
  3. -Tests: runs the EditMode tests (Assets/Tests/Editor) and fails if any test fails.
  4. Builds the player via BuildScript.BuildWindowsPlayer (development, Mono, x64) into out/client/.
  5. -Smoke: runs the player once per smoke view (windowed 1280x720), each writing out/smoke/<view>.png and
     out/smoke/<view>.log, and fails unless every run exits 0 (no errors logged) and wrote its screenshot:
     arena (generated arena preview), sandbox (Laboratory first-person duel with the demonstration pilot),
     shop, draft (Laboratory round 1 screens), hud (duel fast-forwarded into the final 15 s), reveal and results.
     -SmokeOnly <names> runs a subset. out/client-shot.png is a copy of the arena view (older tooling reads it).

  Every Unity step writes its own log under out/ and is failed on a non-zero exit code, compiler errors, or a
  missing success marker in the log.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/build-client.ps1
  powershell -ExecutionPolicy Bypass -File tools/build-client.ps1 -Tests -Smoke
#>
[CmdletBinding()]
param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe',
    [string]$Output,
    [switch]$SkipSync,
    [switch]$Configure,
    [switch]$Tests,
    [switch]$Smoke,
    [string[]]$SmokeOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'client'
$outDir = Join-Path $repo 'out'
if (-not $Output) { $Output = Join-Path $outDir 'client\Confluence.exe' }
$Output = [System.IO.Path]::GetFullPath($Output)

if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found at $UnityPath (pass -UnityPath)." }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Log lines that always mean failure, whatever the exit code says.
$fatalPatterns = @(
    'error CS\d{4}',
    'Scripts have compiler errors',
    'another Unity instance is running with this project open',
    'No valid Unity Editor license found',
    'Aborting batchmode due to failure',
    '\[Build\] RESULT Failed'
)

function Invoke-Unity([string]$step, [string[]]$arguments, [string]$successPattern) {
    $log = Join-Path $outDir "unity-$step.log"
    if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }
    $all = @('-batchmode', '-nographics', '-projectPath', $project, '-logFile', $log) + $arguments
    Write-Host "Unity $step -> $log"
    $process = Start-Process -FilePath $UnityPath -ArgumentList (Join-Arguments $all) -Wait -PassThru -NoNewWindow
    $code = $process.ExitCode
    if (-not (Test-Path -LiteralPath $log)) { throw "Unity $step wrote no log (exit $code)." }

    $text = Get-Content -LiteralPath $log -Raw
    foreach ($pattern in $fatalPatterns) {
        $hits = [regex]::Matches($text, "(?m)^.*$pattern.*$")
        if ($hits.Count -gt 0) {
            $shown = ($hits | Select-Object -First 15 | ForEach-Object { '  ' + $_.Value.Trim() }) -join "`n"
            throw "Unity $step failed ($pattern):`n$shown`nSee $log"
        }
    }
    if ($code -ne 0) { throw "Unity $step exited with code $code. See $log" }
    if ($successPattern -and -not [regex]::IsMatch($text, $successPattern)) {
        throw "Unity $step finished without its success marker '$successPattern'. See $log"
    }
    return $log
}

function Join-Arguments([string[]]$arguments) {
    # Start-Process takes one command line; quote anything with spaces.
    return ($arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ } }) -join ' '
}

# ---- 1. Sync -----------------------------------------------------------------------------------------------
if (-not $SkipSync) {
    & (Join-Path $PSScriptRoot 'sync-unity.ps1')
}

# ---- 2. Configure ------------------------------------------------------------------------------------------
if ($Configure) {
    Invoke-Unity 'configure' @('-quit', '-executeMethod', 'Confluence.Client.EditorTools.ProjectConfigurator.Configure') '\[Configure\] Project configured' | Out-Null
}

# ---- 3. EditMode tests ---------------------------------------------------------------------------------------
if ($Tests) {
    $results = Join-Path $outDir 'unity-editmode.xml'
    if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results -Force }
    # -runTests exits by itself (no -quit); exit code 2 means failing tests, so check the XML instead.
    $log = Join-Path $outDir 'unity-editmode.log'
    if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }
    $all = @('-batchmode', '-nographics', '-projectPath', $project, '-logFile', $log, '-runTests', '-testPlatform', 'EditMode', '-testResults', $results)
    Write-Host "Unity editmode tests -> $results"
    $process = Start-Process -FilePath $UnityPath -ArgumentList (Join-Arguments $all) -Wait -PassThru -NoNewWindow
    if (-not (Test-Path -LiteralPath $results)) { throw "EditMode run produced no results (exit $($process.ExitCode)). See $log" }
    [xml]$xml = Get-Content -LiteralPath $results -Raw
    $run = $xml.'test-run'
    Write-Host "  EditMode: total=$($run.total) passed=$($run.passed) failed=$($run.failed) skipped=$($run.skipped) result=$($run.result)"
    if ([int]$run.failed -gt 0 -or [int]$run.total -eq 0) {
        $failed = $xml.SelectNodes("//test-case[@result='Failed']") | ForEach-Object { '  ' + $_.fullname + ': ' + $_.failure.message.InnerText.Trim() }
        throw "EditMode tests failed:`n$($failed -join "`n")"
    }
}

# ---- 4. Player build ---------------------------------------------------------------------------------------
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Output) | Out-Null
Invoke-Unity 'build' @('-quit', '-executeMethod', 'Confluence.Client.EditorTools.BuildScript.BuildWindowsPlayer', '-buildOutput', $Output) '\[Build\] RESULT Succeeded' | Out-Null
if (-not (Test-Path -LiteralPath $Output)) { throw "Build reported success but $Output is missing." }
Write-Host "Player: $Output"

# ---- 5. Smoke runs -----------------------------------------------------------------------------------------
# Each view: name, player switches, and real seconds before quitting (screens that generate an arena hold the
# screenshot until it is released, see App/SmokeTestDriver.cs). Duel views freeze after the fast-forward
# (-labFreeze) so the frame is the same on every run: 3.84 s is the dummy's guarded hit during the strike.
$smokeViews = @(
    @{ Name = 'arena'; Args = @('-arenaPreview', '-previewView', 'overview', '-arenaSeed', '7919', '-arenaDiameter', '32', '-arenaBiome', 'BIO-001'); Quit = 8 },
    @{ Name = 'sandbox'; Args = @('-laboratory', '-labScreen', 'duel', '-labDemo', '-labDuelTime', '3.84', '-labFreeze'); Quit = 9 },
    @{ Name = 'shop'; Args = @('-laboratory', '-labScreen', 'shop', '-labDemo'); Quit = 8 },
    @{ Name = 'draft'; Args = @('-laboratory', '-labScreen', 'draft', '-labDemo'); Quit = 8 },
    @{ Name = 'hud'; Args = @('-laboratory', '-labScreen', 'duel', '-labDemo', '-labDuelTime', '78', '-labFreeze'); Quit = 9 },
    @{ Name = 'reveal'; Args = @('-laboratory', '-labScreen', 'reveal'); Quit = 8 },
    @{ Name = 'results'; Args = @('-laboratory', '-labScreen', 'results', '-labDemo'); Quit = 8 }
)
if ($Smoke) {
    # -File passes "a,b" as one string: accept commas as well as a PowerShell array.
    $SmokeOnly = @($SmokeOnly | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
    $smokeDir = Join-Path $outDir 'smoke'
    New-Item -ItemType Directory -Force -Path $smokeDir | Out-Null
    foreach ($view in $smokeViews) {
        if ($SmokeOnly -and -not ($SmokeOnly -contains $view.Name)) { continue }
        $shot = Join-Path $smokeDir ($view.Name + '.png')
        $playerLog = Join-Path $smokeDir ($view.Name + '.log')
        foreach ($f in @($shot, $playerLog)) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } }
        $all = $view.Args + @('-screenshot', $shot, '-quitAfter', [string]$view.Quit, '-screen-width', '1280', '-screen-height', '720',
            '-screen-fullscreen', '0', '-logFile', $playerLog)
        Write-Host "Smoke $($view.Name) -> $shot"
        $process = Start-Process -FilePath $Output -ArgumentList (Join-Arguments $all) -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Smoke $($view.Name) exited with code $($process.ExitCode) (errors were logged). See $playerLog" }
        if (-not (Test-Path -LiteralPath $shot)) { throw "Smoke $($view.Name) wrote no screenshot. See $playerLog" }
        if ($view.Name -eq 'arena') { Copy-Item -LiteralPath $shot -Destination (Join-Path $outDir 'client-shot.png') -Force }
        Write-Host "  screenshot: $shot"
    }
}

Write-Host 'build-client: done.'
