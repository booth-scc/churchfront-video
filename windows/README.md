# Churchfront Video - Windows port

C#/.NET port of the macOS SwiftUI app in `../Sources`, aimed at the booth's Windows 11 live-stream PC. The Swift sources stay untouched so upstream merges remain cheap; everything Windows lives under `windows/`.

## Status

Phase 0/1 in progress. ATEM control and NDI send/receive work end to end, VISCA over IP (UDP 52381) is implemented with a fake-camera test suite, and the operator screen mirrors the macOS layout: NEXT and LIVE on top, a centred strip of live source thumbnails, and one bottom bar with the camera chip, exposure, per-camera shots, framing pad, and FADE / CUT / BLACK / LOWER THIRDS. An Engineer view (same window, toggled from the header) maps NDI feeds to inputs, edits cameras and tests VISCA replies. Settings persistence and the tracker are still to come.

## Engineer view

Toggle it in the header, or launch with `--engineer`. Two pages:

- **Setup** - scan NDI, assign a feed to each input (saved to `pictures`), edit camera name/address/port/input/first preset, test a camera's VISCA reply, change the switcher address.
- **Status** - what the app can see: NDI sources found, camera reply test for all cameras, switcher address.

Every edit is written straight to `config.json`, and the operator window reloads its tiles and feeds without restarting.

## Operator shortcuts and saving

- **Space** cuts, **Return** fades, matching the hints on the buttons.
- The gear dialog saves the current camera view into camera presets **20-23** and recalls them, leaving the Stream Deck's 1-9 untouched.

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

## Packaging

```powershell
powershell -ExecutionPolicy Bypass -File windows\build.ps1
```

Publishes a self-contained `win-x64` build into `windows\dist\publish` (no .NET runtime needed on the booth PC) and compiles `windows\dist\ChurchfrontVideo-Setup-0.1.0.exe` (about 55 MB) with Inno Setup.

The installer:

- installs per machine to `Program Files\Churchfront Video`, with Start-menu entry, optional desktop icon and optional start-with-Windows
- warns if the NDI 6 runtime is missing but continues
- keeps `%APPDATA%\CueVideo` on uninstall, so the setup survives upgrades

Requires Inno Setup 6: `winget install JRSoftware.InnoSetup`.

The app connects to `192.168.10.159` by default; override it with `CUEVIDEO_ATEM`.

## Working without the switcher or cameras

```powershell
# terminal 1: a stand-in ATEM on loopback and six clearly different NDI feeds
dotnet run --project windows\src\CueVideo.Spike -- fake-atem 9910
dotnet run --project windows\src\CueVideo.Spike -- fake-ndi-multi 6 7200

# terminal 2: point the UI at them
$env:CUEVIDEO_ATEM = "127.0.0.1"
dotnet run --project windows\src\CueVideo.App
```

The six fake feeds are named `CueVideo CAM1` â€¦ `CueVideo CAM6`; map them to inputs in `pictures` in the config. `CUEVIDEO_NDI_NEXT` / `CUEVIDEO_NDI_LIVE` still override a tile with one fixed source when needed.

`dotnet run --project windows\src\CueVideo.Spike -- atem 192.168.10.159 8` prints the real handshake, program/preview and link transitions without opening the UI. `dotnet run --project windows\src\CueVideo.Spike -- ndi 4` lists the NDI sources currently on the network.

## Configuration

`%APPDATA%\CueVideo\config.json`, same schema as the macOS app (`atemHost`, `me`, `sources`, `cameras`, `keys`, `pictures`, `trackSpeed`, ...), with unknown keys preserved and a `config.last-good.json` fallback. On first run the audited booth setup is written as the default. `CUEVIDEO_CONFIG_DIR` points the app at another folder.

Camera preset slots start at `firstPreset` (20) so the Stream Deck keeps 1-9; the app recalls 1-9 with their configured names ("Full Stage", "Sermon").

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
- **Root `.gitignore`** anchors the macOS `.app` patterns (`/CueVideo.app/`). Windows Git folds case, so the unanchored pattern was silently ignoring `windows/src/CueVideo.App`.

## Gate checklist (see `../.opencode/plan/churchfront-windows-port.md`)

1. G1 - one camera NDI|HX stream at 30 fps for 10 minutes
2. G2 - ATEM tally mirrors Companion within 200 ms
3. G3 - VISCA position/zoom/focus inquiries answered on 52381
4. G4 - OBS program arrives over NDI while streaming

