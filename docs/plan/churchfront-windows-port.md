# Churchfront Video - Windows port plan

Status: in progress. Phase 0/1 implemented; see `windows/README.md`. Upstream: `techwiz18/churchfront-video` (Apache-2.0, SwiftUI, macOS 14+).
Target: the Windows 11 live-stream PC in the booth. The church maintains its own fork.

---

## 1. Goal and non-goals

**Goal.** Give the booth operator the same surface the Mac app gives, on Windows: ATEM state (Next/Live, cut, fade, black, keys), camera control bound to the Next source (joystick, zoom, focus, exposure, shots, presets), AI auto-tracking, stage scan, and live pictures - alongside Companion/Stream Deck and OBS, without disturbing either.

**Non-goals for v1** (left to Companion on purpose): aux routing screens, route buttons, macros, Full Switcher page, DSK routing. No Canon support, no Keychain, no Bonjour. No DeckLink access. No changes to OBS scenes, filters, stream or recording.

---

## 2. Verified environment (read-only audit, 2026-11-20)

Evidence: registry uninstall keys, Companion `logs/companion-2026-11-19.0.log` and `v4.2/db.sqlite`, OBS logs and profile, ATEM autosave XML under `Documents\ATEM Autosave`, PnP device list. Full hardware breakdown: `hardware-inventory.md`.

| Item | Value |
|---|---|
| PC | MSI Z790 GAMING PLUS WIFI, i7-12700KF (12C/20T), 32 GB DDR5 (running 4800), RTX 5060 8 GB, 4 TB NVMe with 3.5 TB free, Win 11 Pro 25H2 build 26200 |
| Displays | 1920x1080 primary (Dell S2415H), 1280x720 secondary via an LKV HDbitT extender |
| Network | Intel I226-V 1 Gbps, 192.168.10.101/24 |
| Switcher | **ATEM 1 M/E Constellation HD** at 192.168.10.159:9910 |
| ATEM software | 10.1.1 (device firmware version still to confirm) |
| ATEM inputs | 1-4 = CAM1-CAM4, 5 = PRES MAIN, 6 = PRES STAGE, 7-10 unused |
| ATEM keys | 4 upstream keyers in the profile; Key 1 on air, media-player fill |
| Capture | **DeckLink Mini Recorder 4K** (single SDI input), Desktop Video 15.3 |
| OBS | 32.0.4, 1080p30, NVENC H.264 stream / AV1 record, YouTube RTMP; scene = DeckLink input + NDI lower third from the FOH Mac |
| Cameras | 3x **BirdDog X4 Ultra** (Companion Cam-1, Cam-3, Cam-4) + 1x **MAKI Ultra** (Cam-2S, the static one) |
| Camera IPs | Cam-1 = .183, Cam-2S (MAKI) = .137, Cam-3 = .149, Cam-4 = .136 |
| Camera control today | Companion 4.2.2 at 192.168.10.101:8000, `birddog-ptz` 3.6.2 connected to all four |
| Stream Deck | Elgato Stream Deck XL (8x4), 11 Companion pages, no triggers |
| Presets in use | Stream Deck uses 1-9 per camera, 8 = "Full Stage", 9 = "Sermon"/"Crowd Wide" |
| NDI | NDI 6 Tools 6.2.1 installed; DistroAV 6.1.1 inside OBS; FOH NDI source already received |
| Other network | ProPresenter 192.168.10.204:1025, OBS WebSocket 192.168.10.101:4455, B-Station .31 / .210 |
| Toolchain | .NET SDK 10.0.404 installed for the port (the original 8.0.301 SDK is end-of-support), Git 2.56, Avalonia 12.1.6 |

### Unverified (the cameras were powered down during the audit)

- Whether NDI|HX output is enabled on the cameras. Without it there are no per-camera pictures.
- Whether VISCA position / zoom / focus **inquiries** are answered on UDP 52381. Companion only proves commands work.
- Camera preset counts, and which camera sits on which ATEM input.
- ATEM firmware version and DSK count.

These are Phase 0 gates, not plan blockers.

### 2.1 Companion structure (configuration export reviewed 2026-11-20)

11 pages, 32-button Stream Deck XL, no triggers. Camera presets 1-9 are already claimed.

| Page | Contents |
|---|---|
| Home | camera page buttons; ATEM program/preview per input 1-6 (short names); AUTO; CUT; OBS scene "Lower 3rd"; Connect Refresh; Reboot Companion; Lighting |
| Cam-1..4 Control | presets 1-7 plus "Full Stage" (8) and "Sermon"/"Crowd Wide" (9); AUTO; CUT; Lower 3rd |
| Cam-1..4 Setup | save presets 1-9; D-pad with pan/tilt speed and HOME; zoom in/out with zoom speed; focus near/far; focus mode toggle |
| Lighting | OSC to B-Station at .210 (`/B1/On` ... `/button/on`) |
| Testing Grounds | scratch page |

- Camera instances: `model: Auto`, no explicit VISCA port, so the module defaults to UDP 52381.
- ATEM instance: `bonjourHost 192.168.10.159:9910`, `autoModelID 18` (1 M/E Constellation HD), ATEM camera control disabled.
- The Cam-1 variables page reports "no variables" while the cameras are offline. The module publishes speed/position variables only when connected, so this is a hint, not proof, that VISCA readback works. Gate 3 still decides.
- Consequence for the port: the app's camera panel should mirror the Companion mental model (D-pad, zoom in/out, focus near/far, presets) so volunteers recognise it, and it must not touch presets 1-9.
- Full page-by-page capture, including every button action and feedback: `companion-capture.md`, with the raw export under the machine-local `%USERPROFILE%\.opencode\plan\companion\`.

### 2.2 OBS structure (read-only review 2026-11-20)

- Scene "Atem" = DeckLink Mini Recorder 4K only ("Auto" mode, SDI). Scene "Lower 3rd" = the same DeckLink plus NDI source `SCCS-MAC-MINI.LOCALDOMAIN (PP Lower Third)`. All sources locked.
- Audio: all stream audio is the ATEM embedded SDI audio arriving through the DeckLink. Both global devices (Desktop Audio, Mic/Aux) are muted, so the app has no audio role at all.
- DistroAV is installed but **program NDI output is disabled**; names preconfigured as "OBS PGM" and "OBS Preview", tally enabled. Enabling program output is one checkbox, so gate 4 is cheap and reversible.
- 1080p30, advanced output, NVENC H.264 for stream and AV1 for recording, no filters, no groups, auto-scene-switcher off.

---

## 3. Corrections to the earlier assumptions

1. **No multiview NDI.** The Constellation HD is SDI-only and the single-input DeckLink is already owned by OBS. Per-camera NDI is the *only* source for Next, camera tiles and tracker frames, so gates 1 and 3 are load-bearing.
2. **Live monitor is OBS program.** OBS Virtual Camera has never been started on this machine (no device registered). Primary transport is DistroAV program NDI output; virtual camera is the fallback. Either way the Live view includes the FOH lower third, i.e. the real stream composite.
3. **NDI runtime DLL** is `Processing.NDI.Lib.x64.dll` (NDI 6.2.1), not `libndi.dll`. Load from `%ProgramFiles%\NDI\NDI 6 Tools\Runtime`; require the NDI runtime as an installer prerequisite instead of vendoring the DLL.
4. **ONNX Runtime DirectML, not CUDA.** The RTX 5060 is sm_120 and DirectML avoids CUDA/cuDNN version pinning. 5-10 Hz pose inference is a small fraction of this GPU.
5. **Tracking covers 3 cameras.** The MAKI Ultra gets a tile and manual zoom/focus only.
6. **Target `net10.0`** with Avalonia 12.x, `win-x64`. The original plan said net8.0 because that was the only SDK present, but .NET 8 left support in November 2026; the .NET 10 SDK is now installed alongside it.
7. Camera traffic must not rely on DHCP: reserve .136/.137/.149/.183 and the ATEM.

---

## 4. Solution layout

Repo: `C:\dev\churchfront-video`, a fork of upstream with Apache-2.0 headers and NOTICE preserved. Solution `ChurchfrontVideo.sln`:

| Project | Responsibility |
|---|---|
| `CueVideo.Atem` | ATEM UDP 9910 client: handshake, state store, packet dedup, commands (program/preview, cut, auto, fade rate, black, upstream key on/off and next-transition) |
| `CueVideo.Visca` | VISCA-IP UDP 52381 (8-byte header, sequence, reply matching) plus raw TCP 5678; inquiry parsing; `ViscaUnits` calibration table ported from `PTZ.swift` |
| `CueVideo.Ndi` | P/Invoke to `Processing.NDI.Lib.x64.dll`: find/receive (bandwidth lowest for tiles), conversion to BGRA, frame callback on a dedicated thread, source discovery for Settings |
| `CueVideo.Vision` | ONNX Runtime DirectML: pose model, optional re-ID embedding, tracker maths ported from `Tracker.swift` |
| `CueVideo.Core` | Config store, camera and shot model, tracking orchestrator, stage scan, preset policy, optional HTTP control server (127.0.0.1:9911 with token) |
| `CueVideo.App` | Avalonia UI: Operator tab and Settings/Status for v1; Engineer pages deferred |
| `CueVideo.Tests` | Unit tests for VISCA encode/decode, ATEM packets, tracker maths, config migration |
| `tools/` | Port of `Tools/fake_atem.py` and the fake VISCA cameras (pure Python, runs unchanged on Windows) |

Port these in shape, rewriting only the platform edge: `ATEM.swift` (442 lines), the VISCALink + ViscaUnits + preset parts of `PTZ.swift`, `Tracker.swift` maths (712 lines), `Config.swift` semantics including `config.last-good.json`. `CanonLink`, `CameraLogin` and the Canon picture path are dropped (roughly 750 lines).

Design tokens from `churchfront-kit` (colours, spacing, Figtree) copy into `CueVideo.App/Styles`.

---

## 5. Config compatibility

- Path: `%APPDATA%\CueVideo\config.json`, same schema as the macOS `~/Library/Application Support/CueVideo/config.json`, plus a `config.last-good.json` fallback when parsing fails.
- The loader must accept a macOS config file unchanged; unknown keys are preserved on save.
- Windows-only keys are additive and optional: `ndi.namePrefix` per camera, `obs.liveSource`, `tracker.device` (`dml` or `cpu`).
- `firstPreset` = **20**, so the app never writes Stream Deck slots 1-9. Recommend 20-39 for app shots and leave 10-19 spare.
- The app **recalls** 1-9 like the Stream Deck does (8 = Full Stage, 9 = Sermon / Crowd Wide), and labels them by name rather than number.

---

## 6. Phase 0 - spike on the booth PC (~2 days)

Run outside service hours; this machine runs the stream. Each gate is a throwaway Avalonia window plus a console harness.

| Gate | Test | Pass criteria | Fallback |
|---|---|---|---|
| **G1 NDI picture** | Receive one camera NDI\|HX stream in a trivial Avalonia window | Stable 30 fps for 10 min, under 150 ms glass-to-glass, under 10 percent of one core at 640x360 proxy | Enable NDI\|HX in the BirdDog web UI; check licence and bandwidth; last resort add an NDI encoder |
| **G2 ATEM tally** | Read program/preview, cut from Companion, watch the mirror | State reflects within 200 ms, no dropped or duplicated packets in 30 min | Reuse the upstream dedup fix; verify protocol version from the handshake |
| **G3 VISCA readback** | Pan/tilt (81 09 06 12 FF), zoom (81 09 04 47 FF), focus (81 09 04 48 FF) inquiries on 52381; then absolute move plus readback | Replies decode to sane values; commanded vs actual within 0.5 deg pan/tilt and 2 percent zoom | **A:** BirdDog REST API 2.0 for state. **B:** image-space closed-loop control, about 2-3 days of tracker rework |
| **G4 OBS program** | Enable the existing DistroAV program NDI output ("OBS PGM"), receive it in the same window as G1 | 1080p30 program received while OBS streams, no OBS frame drops | OBS Virtual Camera capture |

Gate 4 note: program NDI output is a global OBS setting. Confirm with the volunteer team before leaving it enabled.

Record during Phase 0: ATEM firmware, camera models/firmware/preset counts, NDI source names, and OBS stats running alongside to prove the app does not disturb the stream.

---

## 7. Phase 1 - engine (~1.5 weeks)

1. ATEM client: handshake and keepalive, full state store, packet dedup, the commands in section 1. Unit-test against `fake_atem.py`.
2. VISCA client: header/sequence/reply matching, timeouts (no retry for moves), inquiry polling at 10 Hz, command queue with coalescing for joystick streams, `ViscaUnits` calibration, preset recall and store, exposure controls (iris, gain, shutter, WB) matching the upstream UI semantics.
3. NDI module: discovery, per-source bandwidth policy, BGRA conversion, drop-latest frame semantics, clean shutdown.
4. Vision module: ONNX DirectML session, pose decode, tracker port (shot sizing, dead zone, lead room, eased starts, speed learning, identity matching), pluggable appearance embedding to replace `VNGenerateImageFeaturePrint`.
5. `CueVideo.Core` orchestration: tracking follows the Next source; manual override detection (readback diverges from the commanded position, so pause tracking for N seconds); preset policy; stage-scan state machine.
6. Optional: port the HTTP control server (9911 with token) and the MCP stdio server so existing Claude prompts keep working. Defer if time-boxed.
7. Tests and fake-device end-to-end run on Windows.

## 8. Phase 2 - operator UI (~1.5 weeks)

Avalonia Operator tab, designed at 1920x1080, DPI-aware, usable at 1280x720:

- Program and Next monitors (Live = OBS program NDI, Next = camera NDI), tally borders, source name and input number.
- Source row: four cameras plus PRES MAIN/STAGE; click sets preview; Cut, Auto with fade rate, Black, and four key buttons with on-air state.
- Camera panel bound to Next: joystick pad (press and hold, speed ramps with time), zoom rocker, focus near/far plus auto, exposure controls, shot size selector, preset grid limited to 20 and up.
- Status strip: ATEM link, per-camera VISCA link, NDI fps, tracking state, override indicator.
- Settings: IPs, NDI name prefixes, preset base, OBS source, model device, calibration values.
- Keyboard and mouse only, no touch assumptions.

## 9. Phase 3 - tracking and booth tuning (~2 weeks)

1. Calibrate each X4 Ultra: counts-per-degree, zoom-to-FOV curve, lens FOV, mount height and offset.
2. Tune shot sizing per camera and seating area; tune dead zone and lead room against real walk paths.
3. Stage scan: grid sweep, subject detection, map build, per-camera coverage; target under 3 minutes for three cameras.
4. Validate identity matching across a full service (people leaving and returning, similar clothing).
5. Override behaviour with Stream Deck preset recalls and manual joystick moves: the app must not fight the operator.
6. Two full-service soak tests with OBS streaming, watching OBS dropped frames and CPU/GPU headroom.

## 10. Phase 4 - packaging (~3 days)

- Self-contained `win-x64` publish; one `setup.exe` via Inno Setup, per-machine install to Program Files, Start menu shortcut, no admin needed at runtime.
- Installer checks the NDI 6 runtime (warn and link if missing); ONNX models live under `%LOCALAPPDATA%\CueVideo\models`.
- First-run wizard: ATEM IP scan, NDI source discovery, camera assignment, preset base.
- Uninstall leaves `%APPDATA%\CueVideo` config intact.

---

## 11. Acceptance tests

| # | Test | Pass |
|---|---|---|
| A1 | Companion cuts; the app mirrors program and preview | 200 ms or less, 30 min with zero desync |
| A2 | App cut/fade/black reaches the ATEM and OBS | Visible in OBS within one frame |
| A3 | Next preview plus four tiles at 1080p30 | 30 fps sustained 10 min, no drift |
| A4 | Absolute VISCA move then readback | 0.5 deg pan/tilt, 2 percent zoom |
| A5 | Tracking, 5 min walk across the stage | Subject inside the dead zone 90 percent of the time, no oscillation |
| A6 | Stage scan on three cameras | Finishes under 3 min with a usable map |
| A7 | macOS `config.json` loads unchanged | No error, no key loss on save |
| A8 | Installer on a clean Windows 11 VM | Installs, launches, first-run wizard completes |
| A9 | Two-hour soak during a live stream | OBS zero dropped frames, app CPU under 15 percent, GPU under 40 percent |
| A10 | Stream Deck preset recall while tracking | Tracking pauses, no fight, resumes per policy |

---

## 12. Ownership boundaries (from day one)

- **DeckLink:** never opened by the app; OBS is the only client.
- **OBS:** read-only through program NDI output (or virtual camera). No scene, filter, stream or recording changes.
- **ATEM:** the app is a normal ATEM client, reading state and sending switching and key commands. Companion owns auxes, macros and routing.
- **Presets:** the app uses 20 and up (`firstPreset`), the Stream Deck keeps 1-9. Confirmed from the exported configuration.
- **Cameras:** the app polls inquiries and moves only when the operator or the tracker asks; no background nudging.

## 13. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| VISCA inquiries unanswered | Blocks absolute tracking and stage scan | Gate 3, then REST fallback or image-space closed loop |
| Camera NDI\|HX off or unreliable | No pictures for tiles or tracking | Gate 1, enable in the camera UI, last resort add an NDI encoder |
| Sharing the live-stream PC | App load disturbs a service | Measure in Phase 0 and 3 with OBS streaming, cap thread counts, DirectML only |
| NDI runtime upgraded by volunteers | App fails after an NDI update | Detect the runtime version at startup, clear error, document the 6.x minimum |
| DHCP renumbering | Cameras vanish | Reservations plus discovery fallback in Settings |
| Fork divergence from upstream | Manual merges | Keep engine files close to upstream structure, isolate the UI wholesale |

---

## 14. Open items for the booth

1. Power the cameras: confirm NDI|HX status, preset counts, firmware. IP mapping is already known.
2. Run gate 3 (VISCA inquiries), the decisive technical question.
3. ATEM firmware version and DSK count.
4. Confirm volunteers are happy for the app to own joystick/zoom while the Stream Deck keeps presets 1-9.
5. Resolve the Cam-2S preset mismatch (control page recalls 1/2 for Full Stage and Crowd Wide, setup page saves 8/9).
5. Confirm the team accepts DistroAV program NDI output being enabled permanently.
6. Agree a maintenance window for Phase 0 and 3 testing (not Sunday morning).

## 15. Effort

Phase 0 about 2 days, Phase 1 about 1.5 weeks, Phase 2 about 1.5 weeks, Phase 3 about 2 weeks, Phase 4 about 3 days.
v1 total **2.5-3.5 weeks focused**, plus 2-3 days if gate 3 fails and tracking must go image-space.

## 16. Immediate next steps (need a build agent, not plan mode)

1. `winget install --id Git.Git` and verify `git --version`.
2. `git clone --depth 1 https://github.com/techwiz18/churchfront-video C:\dev\churchfront-video`, then re-point origin at the church fork.
3. Scaffold `ChurchfrontVideo.sln` with the projects from section 4 and copy the design tokens.
4. Build the Phase 0 harness (ATEM tally, NDI receive, VISCA inquiry dumper) as one console app so a single booth session settles all four gates.

## 17. Build setup - done 2026-11-20

All four steps above are complete except the fork remote.

| Item | Result |
|---|---|
| Git | 2.56 installed via winget; clone at `C:\dev\churchfront-video`, commit `2c210fb`, remote renamed `upstream` |
| .NET | SDK 10.0.404 installed (Avalonia template targets net10.0; .NET 8 is EOL) |
| Solution | `windows/ChurchfrontVideo.sln`, 8 projects, builds clean with 0 warnings |
| App shell | Avalonia 12.1.6 operator layout: program/next tiles, source row, camera panel with D-pad/zoom/focus/presets, transport row, status chips |
| Spike | `CueVideo.Spike` doctor: runtime, NDI runtime detection, config path, OBS presence, booth endpoints |
| Docs | `windows/README.md` (layout, build, divergences, gates) |
| ATEM engine | `CueVideo.Atem` complete: handshake, ack/retransmit, packet dedup, full state mirror, command encoders; unit tests plus a loopback fake switcher |
| NDI engine | `CueVideo.Ndi` complete: runtime discovery, finder, recv with proxy bandwidth, sender, and a live Avalonia tile. Verified with a loopback unit test and a cross-process test pattern in the Next tile |
| VISCA engine | `CueVideo.Visca` complete for UDP 52381: sequence header, inquiry matching by reply shape, position (counts and degrees), exposure tables, presets with `firstPreset`, absolute moves, focus near/far. Loopback tests against a fake camera |
| Engineer | Engineer window: NDI feed mapping per input (saved to `pictures`), camera address/port/input/preset editing, per-camera VISCA test, switcher address, live NDI source count. Writes `config.json` (except in a demo/self-test run) and reloads the operator view without a restart |
| Packaging | `windows\build.ps1` publishes self-contained win-x64 and compiles an Inno Setup installer (`dist\ChurchfrontVideo-Setup-0.1.0.exe`, ~55 MB) with NDI prerequisite check, optional autostart, Start-menu/desktop icons. Install test still to do on an elevated session or clean VM |
| Config | `%APPDATA%\CueVideo\config.json` with the macOS schema, unknown keys preserved, last-good fallback, first-run booth defaults. Polled every half second; a file that will not load is kept aside before any overwrite |
| UI wiring | Replicates the macOS operator screen: NEXT/LIVE monitors, live source thumbnails with NEXT/LIVE badges, camera chip, exposure read-out, per-camera shot rows (saved shots, or named presets), joystick + zoom rocker + Focus, speed slider, FADE/CUT/BLACK/LOWER THIRDS. Gear dialog keeps keys, presets 1-9 and focus near/far |

Gates G1 and G4 are verified at the transport level with a local test pattern; the camera NDI|HX stream and OBS `OBS PGM` still need a real check on the devices. Gate G2 is verified against the fake switcher only, because the ATEM was powered down; `CueVideo.Spike -- atem 192.168.10.159 10` settles it when the console is on.

Gate 3 is now a configuration check rather than a spike: the client and its fake-camera tests pass, so the booth session only needs `ndi`/`visca` confirmation against the real X4 Ultras.

Next implementation work: tracker and stage scan in `CueVideo.Vision` (DirectML pose, tracker maths port), camera tiles bound to config, then packaging.
