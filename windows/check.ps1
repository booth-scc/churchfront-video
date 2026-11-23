# Builds the Windows solution and runs the test suites.
# Usage: powershell -ExecutionPolicy Bypass -File windows\check.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root 'ChurchfrontVideo.sln'

Write-Host 'Building Windows solution...'
dotnet build $solution --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host 'Checking formatting...'
dotnet format $solution whitespace --verify-no-changes --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host 'Running tests...'
dotnet test $solution --no-build --nologo --verbosity minimal
exit $LASTEXITCODE
