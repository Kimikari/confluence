# Regenerates docs/CONTENT_LEDGER.md and docs/content-ledger.json from a fresh full test run.
#   powershell -File tools/ledger.ps1 [-Configuration Debug|Release]
# The ledger is written even when tests fail (failing records show as implemented-tests-failing);
# the script then exits with the test run's exit code.
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$results = Join-Path $root "out/test-results"

Push-Location $root
try {
    if (Test-Path $results) { Remove-Item -Recurse -Force $results }

    dotnet test Confluence.sln -c $Configuration --logger "trx" --results-directory $results
    $testExit = $LASTEXITCODE
    if ($testExit -ne 0) { Write-Warning "dotnet test exited with $testExit; generating the ledger anyway." }

    dotnet run --project src/Confluence.Tools.Ledger -c $Configuration --no-launch-profile -- --root $root --trx $results
    if ($LASTEXITCODE -ne 0) { throw "ledger tool failed with exit code $LASTEXITCODE" }

    exit $testExit
}
finally {
    Pop-Location
}
