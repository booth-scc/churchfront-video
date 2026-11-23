# Builds the Windows solution and runs the test suite.
# Usage: powershell -ExecutionPolicy Bypass -File windows\check.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host 'Building Windows solution...'
dotnet build (Join-Path $root 'ChurchfrontVideo.sln') --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host 'Running tests...'
dotnet test (Join-Path $root 'tests\CueVideo.Tests\CueVideo.Tests.csproj') --no-build --nologo --verbosity minimal
exit $LASTEXITCODE
