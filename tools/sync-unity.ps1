<#
.SYNOPSIS
  Builds the netstandard2.1 game libraries and syncs them, their NuGet dependencies, and the catalogue into the
  Unity client.

.DESCRIPTION
  1. dotnet build -c Release every src/*/*.csproj that targets netstandard2.1 (Unity loads the same DLLs,
     docs/DECISIONS.md D-002).
  2. Copies each library's own DLL and portable PDB into client/Assets/Plugins/Confluence/, plus the
     netstandard DLLs of every NuGet package those libraries reference (LiteNetLib 2.1.4 is always included,
     docs/DECISIONS.md D-004).
  3. Copies docs/confluence/confluence-catalogue.json into client/Assets/StreamingAssets/.

  The binaries and the catalogue copy are gitignored (client/.gitignore); their .meta files are committed. A
  missing .meta is written from a template whose GUID is derived from the file name, so a new library gets
  the same GUID on every machine. Files are only rewritten when their content changed, so an open editor
  reimports nothing unnecessarily. See docs/notes/client.md.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/sync-unity.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$repo = Split-Path -Parent $PSScriptRoot
$pluginDir = Join-Path $repo 'client\Assets\Plugins\Confluence'
$streamingDir = Join-Path $repo 'client\Assets\StreamingAssets'
$catalogueSource = Join-Path $repo 'docs\confluence\confluence-catalogue.json'
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }

# Packages the client always needs even before a library references them.
$requiredPackages = @(@{ Id = 'LiteNetLib'; Version = '2.1.4' })

function Get-ProjectProperty([xml]$project, [string]$name) {
    foreach ($group in $project.Project.PropertyGroup) {
        $node = $group.SelectSingleNode($name)
        if ($node -ne $null -and $node.InnerText) { return $node.InnerText.Trim() }
    }
    return $null
}

function Get-StableGuid([string]$key) {
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $bytes = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes('Confluence.Client/' + $key))
    } finally {
        $md5.Dispose()
    }
    return -join ($bytes | ForEach-Object { $_.ToString('x2') })
}

function Write-TextFile([string]$path, [string]$content) {
    # UTF-8 without BOM and LF endings, the way Unity writes .meta files.
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $content.Replace("`r`n", "`n"), $encoding)
}

function Ensure-Meta([string]$assetPath, [string]$kind) {
    $metaPath = $assetPath + '.meta'
    if (Test-Path -LiteralPath $metaPath) { return }
    $guid = Get-StableGuid ((Split-Path -Leaf (Split-Path -Parent $assetPath)) + '/' + (Split-Path -Leaf $assetPath))
    if ($kind -eq 'plugin') {
        $text = @"
fileFormatVersion: 2
guid: $guid
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 1
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  userData:
  assetBundleName:
  assetBundleVariant:

"@
    } else {
        $text = @"
fileFormatVersion: 2
guid: $guid
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:

"@
    }
    Write-TextFile $metaPath $text
    Write-Host "  wrote $([System.IO.Path]::GetFileName($metaPath)) (guid $guid)"
}

function Copy-IfChanged([string]$source, [string]$destination) {
    if (Test-Path -LiteralPath $destination) {
        $a = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $b = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($a -eq $b) {
            Write-Host "  unchanged $([System.IO.Path]::GetFileName($destination))"
            return
        }
    }
    Copy-Item -LiteralPath $source -Destination $destination -Force
    Write-Host "  copied    $([System.IO.Path]::GetFileName($destination))"
}

# ---- 1. Discover and build the netstandard2.1 libraries ---------------------------------------------------------
$libraries = @()
$packages = @{}
foreach ($pkg in $requiredPackages) { $packages[$pkg.Id.ToLowerInvariant()] = $pkg }

foreach ($csproj in Get-ChildItem -Path (Join-Path $repo 'src') -Filter '*.csproj' -Recurse | Sort-Object FullName) {
    [xml]$xml = Get-Content -LiteralPath $csproj.FullName -Raw
    if ((Get-ProjectProperty $xml 'TargetFramework') -ne 'netstandard2.1') { continue }
    $assemblyName = Get-ProjectProperty $xml 'AssemblyName'
    if (-not $assemblyName) { $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($csproj.Name) }
    $libraries += [pscustomobject]@{ Project = $csproj.FullName; Assembly = $assemblyName; Dir = $csproj.DirectoryName }
    foreach ($ref in $xml.SelectNodes('//PackageReference')) {
        $id = $ref.GetAttribute('Include')
        $version = $ref.GetAttribute('Version')
        if ($id -and $version) { $packages[$id.ToLowerInvariant()] = @{ Id = $id; Version = $version } }
    }
}

if ($libraries.Count -eq 0) { throw "No netstandard2.1 projects found under $repo\src." }

if (-not $SkipBuild) {
    foreach ($lib in $libraries) {
        Write-Host "dotnet build $($lib.Assembly) ($Configuration)"
        & dotnet build $lib.Project -c $Configuration --nologo -v quiet -clp:ErrorsOnly
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for $($lib.Project) (exit $LASTEXITCODE)." }
    }
}

# ---- 2. Copy libraries and package DLLs ----------------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Write-Host "Syncing plugins into $pluginDir"
foreach ($lib in $libraries) {
    $outDir = Join-Path $lib.Dir "bin\$Configuration\netstandard2.1"
    $dll = Join-Path $outDir ($lib.Assembly + '.dll')
    if (-not (Test-Path -LiteralPath $dll)) { throw "Missing build output $dll (run without -SkipBuild)." }
    foreach ($ext in @('.dll', '.pdb')) {
        $source = Join-Path $outDir ($lib.Assembly + $ext)
        if (-not (Test-Path -LiteralPath $source)) { continue }
        $destination = Join-Path $pluginDir ($lib.Assembly + $ext)
        Copy-IfChanged $source $destination
        Ensure-Meta $destination ($(if ($ext -eq '.dll') { 'plugin' } else { 'default' }))
    }
}

foreach ($key in ($packages.Keys | Sort-Object)) {
    $pkg = $packages[$key]
    $pkgDir = Join-Path $nugetRoot (Join-Path $key $pkg.Version)
    $libDir = $null
    foreach ($tfm in @('netstandard2.1', 'netstandard2.0')) {
        $candidate = Join-Path $pkgDir "lib\$tfm"
        if (Test-Path -LiteralPath $candidate) { $libDir = $candidate; break }
    }
    if (-not $libDir) { throw "Package $($pkg.Id) $($pkg.Version) has no netstandard2.x library under $pkgDir (run dotnet restore)." }
    foreach ($file in Get-ChildItem -LiteralPath $libDir -Filter '*.dll' | Sort-Object Name) {
        $destination = Join-Path $pluginDir $file.Name
        Copy-IfChanged $file.FullName $destination
        Ensure-Meta $destination 'plugin'
    }
}

# ---- 3. Catalogue -------------------------------------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $streamingDir | Out-Null
$catalogueDestination = Join-Path $streamingDir 'confluence-catalogue.json'
Write-Host "Syncing catalogue into $streamingDir"
Copy-IfChanged $catalogueSource $catalogueDestination
Ensure-Meta $catalogueDestination 'default'

Write-Host 'sync-unity: done.'
