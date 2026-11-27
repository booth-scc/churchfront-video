# Non-interactive demo check for the Operator screen. Starts the demo booth with the freshly
# built Debug app, drives the camera controls through UI Automation, checks the exposure rows,
# then stops the app so run-demo.ps1 cleans up its helpers.
#
# It refuses to run while OBS or Churchfront Video is already running, and it moves the mouse
# only after bringing the window forward: the joystick needs a real pointer press and release.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$demo = Join-Path $root 'tools\run-demo.ps1'
$crash = Join-Path $env:LOCALAPPDATA 'CueVideo\logs\crash.log'

if (Get-Process obs64 -ErrorAction SilentlyContinue) { throw 'OBS is running. Close it before running the demo smoke.' }
if (Get-Process CueVideo.App -ErrorAction SilentlyContinue) { throw 'Churchfront Video is already running. Close it first.' }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class DemoWin {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
}
"@

function Pass([string]$text) { Write-Host "ok   $text" }

$crashBefore = if (Test-Path $crash) { (Get-Item $crash).LastWriteTime } else { $null }
$existingSpike = @(Get-Process CueVideo.Spike -ErrorAction SilentlyContinue | ForEach-Object Id)
$configPath = Join-Path $env:APPDATA 'CueVideo\demo\config.json'
$originalConfig = $null
$wrapper = Start-Process powershell -ArgumentList @(
    '-ExecutionPolicy', 'Bypass', '-File', "`"$demo`"", '-Build') -PassThru -WindowStyle Hidden
$app = $null
$passed = $false

try {
    $deadline = (Get-Date).AddSeconds(45)
    while (-not $app -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $app = Get-Process CueVideo.App -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($wrapper.HasExited -and -not $app) {
            throw "run-demo.ps1 exited before the app started (exit $($wrapper.ExitCode))."
        }
    }
    if (-not $app) { throw 'The demo app did not start.' }

    $window = $null
    $deadline = (Get-Date).AddSeconds(20)
    while (-not $window -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id)
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children, $condition)
    }
    if (-not $window) { throw 'The operator window did not appear.' }
    Pass 'window opened'

    function Find-Control([string]$id) {
        $window.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))
    }

    function Get-SharedLines([string]$path) {
        $stream = [System.IO.File]::Open(
            $path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $reader = New-Object System.IO.StreamReader($stream)
            try { return $reader.ReadToEnd() -split "`r?`n" } finally { $reader.Dispose() }
        } finally {
            $stream.Dispose()
        }
    }

    function Get-Connections([string]$path) {
        $counts = @{}
        if (Test-Path $path) {
            foreach ($line in (Get-SharedLines $path)) {
                if ($line -match '^connections CueVideo (CAM\d+): (\d+)$') {
                    $counts[$Matches[1]] = [int]$Matches[2]
                }
            }
        }
        return $counts
    }

    function Invoke-Control([string]$id) {
        $element = Find-Control $id
        if (-not $element) { throw "Control '$id' not found." }
        $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 250
        if ($app.HasExited) { throw "The app exited after '$id'." }
    }

    # The fake camera answers exposure inquiries, so the rows fill once its link is up.
    $unknown = @('', '-', [string][char]0x2014)
    $gain = ''
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $gainElement = Find-Control 'ExposureGain'
        $gain = if ($gainElement) { $gainElement.Current.Name } else { '' }
    } while (($gain -in $unknown) -and (Get-Date) -lt $deadline)
    if ($gain -in $unknown) { throw "Exposure did not fill: '$gain'." }
    Pass "exposure read $gain"

    # A hand edit to config.json must reach the screen without a restart.
    $originalConfig = [System.IO.File]::ReadAllText($configPath)
    $editedConfig = $originalConfig.Replace('"label": "PRES MAIN"', '"label": "PRES EDITED"')
    if ($editedConfig -eq $originalConfig) { throw 'The demo config has no PRES MAIN label to edit.' }

    [System.IO.File]::WriteAllText($configPath, $editedConfig, [System.Text.UTF8Encoding]::new($false))

    $edited = $null
    $deadline = (Get-Date).AddSeconds(3)
    while (-not $edited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $edited = $window.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, 'PRES EDITED')))
    }
    if (-not $edited) { throw 'A hand edit to config.json did not reach the screen.' }
    Pass 'config reload'

    # Map input 6 onto the picture input 1 already shows. The fake NDI sender reports how
    # many receivers connect to each source, so this shows one receiver serving both tiles.
    # NDI counts one receiver as two connections on the send side, so compare sources rather
    # than expecting an absolute number.
    $currentConfig = [System.IO.File]::ReadAllText($configPath)
    $withDuplicate = $currentConfig.Replace('"6": "CueVideo CAM6"', '"6": "CueVideo CAM1"')
    if ($withDuplicate -eq $currentConfig) {
        throw 'The demo config has no CAM6 picture to duplicate.'
    }

    [System.IO.File]::WriteAllText($configPath, $withDuplicate, [System.Text.UTF8Encoding]::new($false))
    Start-Sleep -Milliseconds 1500

    $senderLog = Join-Path $env:APPDATA 'CueVideo\demo\ndi-sender.log'
    $connections = @{}
    $shared = $false
    $deadline = (Get-Date).AddSeconds(8)
    do {
        Start-Sleep -Milliseconds 300
        $connections = Get-Connections $senderLog

        # Every source is shown through one receiver, so their connection counts match even
        # though CAM1 also has the second tile. A per-tile receiver would raise CAM1's count.
        # CAM6 dropping to zero proves the duplicate edit reached the running app.
        $shared = $connections['CAM1'] -and
            $connections['CAM1'] -eq $connections['CAM2'] -and
            $connections['CAM1'] -eq $connections['CAM3'] -and
            $connections['CAM6'] -eq 0
    } while (-not $shared -and (Get-Date) -lt $deadline)

    if (-not $shared) {
        throw ("Two tiles on one source did not share one receiver " +
            "(CAM1=$($connections['CAM1']) CAM2=$($connections['CAM2']) " +
            "CAM3=$($connections['CAM3']) CAM6=$($connections['CAM6'])).")
    }
    Pass ("one receiver for CAM1 on two tiles " +
        "(CAM1-3 connections: $($connections['CAM1']),$($connections['CAM2']),$($connections['CAM3']))")

    # Move both CAM1 tiles away from CAM1. Its last view is gone, so the receiver must close.
    $withoutCam1 = $withDuplicate.Replace('"1": "CueVideo CAM1"', '"1": "CueVideo CAM4"').
        Replace('"6": "CueVideo CAM1"', '"6": "CueVideo CAM4"')
    if ($withoutCam1 -eq $withDuplicate) {
        throw 'The demo config did not have CAM1 on both inputs.'
    }

    [System.IO.File]::WriteAllText($configPath, $withoutCam1, [System.Text.UTF8Encoding]::new($false))

    $connections = @{}
    $released = $false
    $deadline = (Get-Date).AddSeconds(8)
    do {
        Start-Sleep -Milliseconds 300
        $connections = Get-Connections $senderLog
        $released = $connections['CAM1'] -eq 0
    } while (-not $released -and (Get-Date) -lt $deadline)

    if (-not $released) {
        throw "CAM1 kept a receiver after its tiles left (connections: $($connections['CAM1']))."
    }
    Pass 'released the unused feed'

    Invoke-Control 'ZoomInButton'; Pass 'zoom in'
    Invoke-Control 'FocusButton'; Pass 'focus'
    Invoke-Control 'ExposureAutoButton'
    Start-Sleep -Seconds 2
    Pass 'auto exposure'

    $buttons = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)),
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, 'Sermon')))))
    if ($buttons.Count -eq 0) { throw 'No shot-row preset button found.' }
    $buttons[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
    if ($app.HasExited) { throw 'The app exited after a shot preset.' }
    Pass "shot preset ($($buttons.Count) rows)"

    # Hold the joystick up for a moment, as the booth does, after bringing the window forward.
    $panUp = Find-Control 'PanUp'
    if (-not $panUp) { throw "Control 'PanUp' not found." }
    $app.Refresh()
    if ($app.MainWindowHandle -ne 0) { [DemoWin]::SetForegroundWindow($app.MainWindowHandle) | Out-Null }
    Start-Sleep -Milliseconds 300
    $rect = $panUp.Current.BoundingRectangle
    [DemoWin]::SetCursorPos([int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 200
    [DemoWin]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    [DemoWin]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
    if ($app.HasExited) { throw 'The app exited after the joystick.' }
    Pass 'joystick hold'

    if (Test-Path $crash) {
        $written = (Get-Item $crash).LastWriteTime
        if (-not $crashBefore -or $written -gt $crashBefore) { throw "Crash log written: $crash" }
    }
    Pass 'no crash log'
    $passed = $true
} catch {
    Write-Host "fail $($_.Exception.Message)"
} finally {
    Get-Process CueVideo.App -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    if ($originalConfig) {
        try { [System.IO.File]::WriteAllText($configPath, $originalConfig, [System.Text.UTF8Encoding]::new($false)) } catch { }
    }
    if ($wrapper -and -not $wrapper.HasExited) {
        try { $wrapper.WaitForExit(15000) | Out-Null } catch { }
    }
    if ($wrapper -and -not $wrapper.HasExited) {
        Stop-Process -Id $wrapper.Id -Force -ErrorAction SilentlyContinue
        Get-Process CueVideo.Spike -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -notin $existingSpike } |
            Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

if ($passed) {
    Write-Host 'Demo smoke passed.'
    exit 0
}

Write-Host 'Demo smoke failed.'
exit 1
