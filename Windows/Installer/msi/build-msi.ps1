<#
.SYNOPSIS
    Publishes Ghozt - Dev Tester and packages it as an MSI.

.DESCRIPTION
    Produces Windows\artifacts\Ghozt-<version>.msi.

    The app is published self-contained so a machine without the .NET 8 Desktop Runtime can still
    run it. Ghozt is installed onto machines in order to examine what is already on them; requiring
    the operator to install a runtime first would mean the tool cannot be used on the very box whose
    configuration is in question.

    Every dependency the app resolves at runtime is carried: the .NET runtime, WPF, and the managed
    libraries. Package.wxs harvests the publish folder wholesale rather than listing files, so a new
    dependency cannot be silently left out of the installer.

.PARAMETER Version
    Defaults to the Version in App\Directory.Build.props, which is the single source of truth.

.PARAMETER SkipPublish
    Reuses an existing publish folder. Only for iterating on the WiX authoring itself.

.EXAMPLE
    .\build-msi.ps1
    .\build-msi.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$productRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$appRoot = Join-Path $productRoot 'App'
$propsPath = Join-Path $appRoot 'Directory.Build.props'

if (-not $Version) {
    $Version = ([xml](Get-Content $propsPath)).Project.PropertyGroup.Version
}
if (-not $Version) { throw "Could not read Version from $propsPath" }

$publishDir = Join-Path $productRoot 'Windows\build\publish\win-x64'
$outputDir = Join-Path $productRoot 'Windows\artifacts'
$msiPath = Join-Path $outputDir "Ghozt-$Version.msi"

# WiX's CAB step fails when TEMP is long, and this machine's default TEMP is very long. Setting a
# short one here rather than relying on the caller having done it: a build that works from one shell
# and not another is a build nobody trusts.
$shortTemp = 'C:\cizt'
New-Item -ItemType Directory -Force -Path $shortTemp | Out-Null
$env:TEMP = $shortTemp
$env:TMP = $shortTemp

# ---------------------------------------------------------------- branding

Write-Host "==> Regenerating brand assets" -ForegroundColor Cyan
$brandScript = Join-Path $productRoot 'Windows\Installer\brand\build-brand-assets.py'
& python $brandScript
if ($LASTEXITCODE -ne 0) { throw "brand asset generation failed with exit code $LASTEXITCODE" }

# ---------------------------------------------------------------- tests

Write-Host "==> Running tests" -ForegroundColor Cyan
& dotnet test (Join-Path $appRoot 'tests\Ghozt.Tests\Ghozt.Tests.csproj') -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "tests failed with exit code $LASTEXITCODE - no installer produced" }

# ---------------------------------------------------------------- publish

if (-not $SkipPublish) {
    Write-Host "==> Publishing $Configuration (self-contained, win-x64)" -ForegroundColor Cyan

    # Stop a running instance first: it locks its own executable, and a build that quietly reuses a
    # stale binary is worse than one that fails.
    Get-Process Ghozt -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    & dotnet publish (Join-Path $appRoot 'src\Ghozt.App\Ghozt.App.csproj') `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:Version=$Version `
        -p:PublishSelfContained=true `
        -o $publishDir `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}

if (-not (Test-Path (Join-Path $publishDir 'Ghozt.exe'))) {
    throw "Ghozt.exe is missing from $publishDir - the publish did not produce a runnable app."
}

# The icon has to reach the publish folder: Package.wxs points ARPPRODUCTICON and both shortcuts at
# it, and wix fails the build if it is absent rather than shipping an unbranded entry.
$iconTarget = Join-Path $publishDir 'Assets\GhoztLogo.ico'
if (-not (Test-Path $iconTarget)) {
    throw "GhoztLogo.ico is missing from the publish output. Check the csproj Content item."
}

$published = Get-ChildItem $publishDir -Recurse -File | Measure-Object -Property Length -Sum
Write-Host ("    {0} files, {1:N1} MB" -f $published.Count, ($published.Sum / 1MB))

# A self-contained WPF publish is several hundred files. Far fewer means the runtime was not carried,
# which produces an installer that works only on the author's machine.
if ($published.Count -lt 100) {
    throw "Only $($published.Count) files published - this does not look self-contained."
}

# ---------------------------------------------------------------- package

# The MSI ProductVersion field only honours major.minor.build, so drop any extra segment.
$msiVersion = ($Version -split '-')[0]
$parts = $msiVersion -split '\.'
if ($parts.Count -gt 3) { $msiVersion = ($parts[0..2] -join '.') }

Write-Host "==> Building MSI $msiVersion" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

& wix build (Join-Path $PSScriptRoot 'Package.wxs') `
    -arch x64 `
    -define "PublishDir=$publishDir" `
    -define "ProductVersion=$msiVersion" `
    -ext WixToolset.Util.wixext `
    -ext WixToolset.UI.wixext `
    -bindpath $PSScriptRoot `
    -out $msiPath
if ($LASTEXITCODE -ne 0) { throw "wix build failed with exit code $LASTEXITCODE" }

$msi = Get-Item $msiPath
Write-Host ""
Write-Host ("==> {0} ({1:N1} MB)" -f $msi.FullName, ($msi.Length / 1MB)) -ForegroundColor Green
Write-Host "    Publisher: CAPITAL-IZ    Product: Ghozt - Dev Tester    Version: $msiVersion"
