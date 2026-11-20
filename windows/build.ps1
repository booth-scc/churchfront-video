# Builds the self-contained app and the installer.
# Usage: powershell -ExecutionPolicy Bypass -File windows\build.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$publish = Join-Path $dist 'publish'

Write-Host 'Publishing CueVideo.App (self-contained win-x64)...'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish (Join-Path $root 'src\CueVideo.App\CueVideo.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -o $publish

if (-not (Test-Path (Join-Path $publish 'CueVideo.App.exe'))) {
    throw 'publish did not produce CueVideo.App.exe'
}

$isccCandidates = @(
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe',
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found. winget install JRSoftware.InnoSetup' }

Write-Host "Compiling installer with $iscc ..."
& $iscc (Join-Path $root 'installer\ChurchfrontVideo.iss')

$setup = Get-ChildItem $dist -Filter 'ChurchfrontVideo-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) { throw 'installer was not produced' }

Write-Host ''
Write-Host "Installer: $($setup.FullName)"
Write-Host "Size:      $([math]::Round($setup.Length / 1MB, 1)) MB"
