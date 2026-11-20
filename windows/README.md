# Churchfront Video - Windows port

C#/.NET port of the macOS SwiftUI app in `../Sources`, aimed at the booth's Windows 11 live-stream PC. The Swift sources stay untouched so upstream merges remain cheap; everything Windows lives under `windows/`.

## Status

Phase 0/1 in progress. The ATEM client works (handshake, state, dedup, commands) and NDI send/receive works end to end (P/Invoke to the NDI 6 runtime, discovery, BGRX frames into the UI). Both are covered by tests, including a loopback NDI sender. VISCA is still an empty project.

## Requirements

- .NET SDK 10 (the .NET 8 SDK that shipped with the booth PC is out of support as of November 2026)
- NDI 6 Tools installed (the app loads `Processing.NDI.Lib.x64.dll` from `%ProgramFiles%\NDI\NDI 6 Tools\Runtime`)
- For live testing: ATEM reachable on UDP 9910, BirdDog cameras on UDP 52381, OBS with DistroAV program NDI output enabled

## Build and run

```powershell
dotnet build windows\ChurchfrontVideo.sln
dotnet run --project windows\src\CueVideo.App
dotnet run --project windows\src\CueVideo.Spike
```

The app connects to `192.168.10.159` by default; override it with `CUEVIDEO_ATEM`.

## Working without the switcher or cameras

```powershell
# terminal 1: a stand-in ATEM on loopback and a moving NDI test pattern
dotnet run --project windows\src\CueVideo.Spike -- fake-atem 9910
dotnet run --project windows\src\CueVideo.Spike -- fake-ndi "CueVideo Test Pattern" 300

# terminal 2: point the UI at them
$env:CUEVIDEO_ATEM = "127.0.0.1"
$env:CUEVIDEO_NDI_NEXT = "CueVideo Test Pattern"
dotnet run --project windows\src\CueVideo.App
```

`CUEVIDEO_NDI_LIVE` works the same way and should point at `OBS PGM` once DistroAV's program output is enabled. Leave both unset for the plain shell.

`dotnet run --project windows\src\CueVideo.Spike -- atem 192.168.10.159 8` prints the real handshake, program/preview and link transitions without opening the UI. `dotnet run --project windows\src\CueVideo.Spike -- ndi 4` lists the NDI sources currently on the network.

## Layout

| Path | Purpose | Ported from |
|---|---|---|
| `src/CueVideo.Atem` | ATEM UDP 9910 client, state store, dedup | `Sources/CueATEM/ATEM.swift` |
| `src/CueVideo.Visca` | VISCA-IP 52381 + raw TCP 5678, units/calibration | `Sources/CueATEM/PTZ.swift` (VISCALink, ViscaUnits) |
| `src/CueVideo.Ndi` | P/Invoke to the NDI runtime, receive + discovery | `Sources/NDIBridge`, `NDIFeed.swift` |
| `src/CueVideo.Vision` | ONNX Runtime DirectML pose/identity | `Tracker.swift`, `Identity.swift` |
| `src/CueVideo.Core` | Config, orchestration, tracking, stage scan | `Config.swift`, `StageScan.swift`, `ControlServer.swift` |
| `src/CueVideo.App` | Avalonia operator UI | `VolunteerView.swift` and friends |
| `src/CueVideo.Spike` | Phase 0 doctor and gate harness | `Tools/fake_atem.py` + `SelfTest.swift` |
| `tests/CueVideo.Tests` | Unit tests | `Tests/` |

## Deliberate divergences from the macOS app

- **No Canon path.** `CanonLink`, `CameraLogin`, `CanonPreview` and the Canon picture case are not ported.
- **No Keychain, no Bonjour.** Endpoints are stored as plain text in `%APPDATA%\CueVideo\config.json` (same schema as macOS, unknown keys preserved).
- **Live monitor** is the OBS program NDI feed ("OBS PGM"), not a DeckLink capture. The app never opens the DeckLink.
- **Presets:** the app recalls 1-9 (same as the Stream Deck, 8 = Full Stage, 9 = Sermon / Crowd Wide) and only writes 20-39.
- **UI stack:** Avalonia 12 on .NET 10 instead of SwiftUI; design tokens copied from `churchfront-kit`.

## Gate checklist (see `../.opencode/plan/churchfront-windows-port.md`)

1. G1 - one camera NDI|HX stream at 30 fps for 10 minutes
2. G2 - ATEM tally mirrors Companion within 200 ms
3. G3 - VISCA position/zoom/focus inquiries answered on 52381
4. G4 - OBS program arrives over NDI while streaming
