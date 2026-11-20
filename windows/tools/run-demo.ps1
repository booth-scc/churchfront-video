# Starts the demo booth: fake ATEM, four fake VISCA cameras and six NDI feeds, then the app.
# Everything it writes lives under %APPDATA%\CueVideo, so nothing depends on the temp folder.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$spike = Join-Path $root 'src\CueVideo.Spike\bin\Debug\net10.0\CueVideo.Spike.exe'
$appExe = Join-Path $root 'dist\publish\CueVideo.App.exe'
if (-not (Test-Path $appExe)) { $appExe = Join-Path $root 'src\CueVideo.App\bin\Debug\net10.0\CueVideo.App.exe' }

$demo = Join-Path $env:APPDATA 'CueVideo\demo'
New-Item -ItemType Directory -Force $demo | Out-Null

$config = @'
{
  "atemHost": "127.0.0.1",
  "me": 1,
  "sources": [
    { "input": 1, "label": "CAM 1" },
    { "input": 2, "label": "CAM 2" },
    { "input": 3, "label": "CAM 3" },
    { "input": 4, "label": "CAM 4" },
    { "input": 5, "label": "PRES MAIN" },
    { "input": 6, "label": "PRES STAGE" }
  ],
  "cameras": [
    { "name": "Cam-1",  "host": "127.0.0.1", "port": 52381, "atemInput": 1, "firstPreset": 20 },
    { "name": "Cam-2S", "host": "127.0.0.1", "port": 52382, "atemInput": 2, "firstPreset": 20 },
    { "name": "Cam-3",  "host": "127.0.0.1", "port": 52383, "atemInput": 3, "firstPreset": 20 },
    { "name": "Cam-4",  "host": "127.0.0.1", "port": 52384, "atemInput": 4, "firstPreset": 20 }
  ],
  "pictures": { "1": "CueVideo CAM1", "2": "CueVideo CAM2", "3": "CueVideo CAM3", "4": "CueVideo CAM4", "5": "CueVideo CAM5", "6": "CueVideo CAM6" },
  "keys": [
    { "label": "KEY 1", "me": 1, "keyer": 0 },
    { "label": "KEY 2", "me": 1, "keyer": 1 },
    { "label": "KEY 3", "me": 1, "keyer": 2 },
    { "label": "KEY 4", "me": 1, "keyer": 3 }
  ]
}
'@

Set-Content -Path (Join-Path $demo 'config.json') -Value $config -Encoding UTF8

if (-not (Test-Path $spike)) {
    [System.Windows.Forms.MessageBox]::Show(
        "The demo helpers are not built.`n`nRun: dotnet build C:\dev\churchfront-video\windows\ChurchfrontVideo.sln",
        'Churchfront Video')
    exit 1
}

Get-Process CueVideo.App -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

$helpers = @()
$helpers += Start-Process -FilePath $spike -ArgumentList 'fake-atem','9910' -PassThru -WindowStyle Hidden
foreach ($port in 52381..52384) {
    $helpers += Start-Process -FilePath $spike -ArgumentList 'fake-visca',$port -PassThru -WindowStyle Hidden
}
$helpers += Start-Process -FilePath $spike -ArgumentList 'fake-ndi-multi','6','36000' -PassThru -WindowStyle Hidden

Start-Sleep -Seconds 2

$env:CUEVIDEO_ATEM = '127.0.0.1'
$env:CUEVIDEO_CONFIG_DIR = $demo

$app = Start-Process -FilePath $appExe -PassThru
Start-Sleep -Seconds 4

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class CueDemoWin {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@

$app.Refresh()
if (-not $app.HasExited -and $app.MainWindowHandle -ne 0) {
    [void][CueDemoWin]::ShowWindow($app.MainWindowHandle, 9)
    [void][CueDemoWin]::SetForegroundWindow($app.MainWindowHandle)
}

$app.WaitForExit()

foreach ($h in $helpers) {
    try { Stop-Process -Id $h.Id -Force -ErrorAction SilentlyContinue } catch { }
}
