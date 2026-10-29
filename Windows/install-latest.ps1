$ErrorActionPreference = 'Stop'
$msi = (Get-ChildItem (Join-Path $PSScriptRoot 'artifacts\Ghozt-*.msi') | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
Write-Host "==> Installing $msi ..." -ForegroundColor Cyan
$msi070 = Join-Path $PSScriptRoot 'artifacts\Ghozt-0.7.0.msi'
if (Test-Path $msi070) {
    Write-Host "==> Uninstalling 0.7.0 ..." -ForegroundColor Yellow
    $uninst = Start-Process msiexec.exe -ArgumentList @('/x', $msi070, '/qn') -Wait -PassThru
    Write-Host "==> Uninstall exit code: $($uninst.ExitCode)"
}

$log = Join-Path $PSScriptRoot 'artifacts\install_080.log'
$proc = Start-Process msiexec.exe -ArgumentList @('/i', $msi, '/qn', '/l*v', $log) -Wait -PassThru
Write-Host "==> msiexec exit code: $($proc.ExitCode)"

if (Test-Path $log) {
    Select-String -Path $log -Pattern 'Product:|MainEngineThread is returning|Installation success or error' | Select-Object -Last 5 | ForEach-Object { Write-Host $_.Line }
}

$exe = 'C:\Program Files\CAPITAL-IZ\Ghozt\Ghozt.exe'
if (Test-Path $exe) {
    (Get-Item $exe).VersionInfo | Format-List ProductVersion, FileVersion, FileName
}
