# Companion configuration capture - 2026-11-20

Purpose: record the booth's Bitfocus Companion setup exactly as it stands, so the Windows port (Churchfront Video) can match the operator's mental model and never collide with the Stream Deck.

**How this was captured.** Companion's own Import/Export screen, all components except secrets, compressed format. Export file name: `SHORESLIVESTREAMPC_2026-11-20-0845_custom_config.companionconfig` (20,835 bytes, gzip). Nothing in Companion was changed; the export is read-only.

The raw export is machine-local, not in this repo:

- `%USERPROFILE%\.opencode\plan\companion\companion-config-2026-11-20.json` - decompressed export (226 KB, secrets excluded)
- `%USERPROFILE%\.opencode\plan\companion\companion-buttons-grid.txt` - flat grid dump used to build the tables below

Software: Companion 4.2.2 build `4.2.2+8755-stable-6b5530ac3d`, database schema v9. Current release is 5.1.3, so this configuration predates a major version.

---

## 1. Surface

| Property | Value |
|---|---|
| Hardware | Elgato Stream Deck XL, serial `A00NA53032G7RX`, 8 columns x 4 rows |
| Group | Auto group, `use_last_page: true`, startup page 1 (Home), last page 4 |
| Brightness | 100, rotation 0 |
| Lock | none (`never_lock: false`, no pincode), page restriction off |

---

## 2. Connections

| Label | Module | Version | Config (secrets omitted) |
|---|---|---|---|
| Atem | bmd-atem | 3.20.1 | `bonjourHost 192.168.10.159:9910`, `autoModelID 18` = "1 M/E Constellation HD", `enableCameraControl: false` |
| Cam-1 | birddog-ptz | 3.6.2 | host 192.168.10.183, model Auto (X4 Ultra) |
| Cam-2S | birddog-ptz | 3.6.2 | host 192.168.10.137, model Auto (MAKI Ultra) |
| Cam-3 | birddog-ptz | 3.6.2 | host 192.168.10.149, model Auto (X4 Ultra) |
| Cam-4 | birddog-ptz | 3.6.2 | host 192.168.10.136, model Auto (X4 Ultra) |
| obs | obs-studio | 3.15.3 | 192.168.10.101:4455 (password stored, excluded here) |
| B-Station_2 | visualproductions-bstation2 | 1.0.8 | 192.168.10.31:7000 |
| B-Station_2_2 | visualproductions-bstation2 | 1.0.8 | 192.168.10.210:7000 |
| osc | generic-osc | 2.8.2 | UDP listener 192.168.10.210:8000 |
| Propresenter-API | renewedvision-propresenter-api | 1.0.5 | 192.168.10.204:1025 |

Connection collection "Cameras" holds Cam-1, Cam-2S, Cam-3, Cam-4. No triggers, no custom variables, no expression variables.

Legend for the page tables: `PGMn` = ATEM program on input n, `PVWn` = ATEM preview, `Pn` = camera preset recall n, `saveN` = camera preset store n, `->pn` = go to page n, `PT:x` = pan/tilt direction x (press/release), `+` = has state feedback.

---

## 3. Page map

| # | Name | Role |
|---|---|---|
| 1 | Home | switching + navigation hub |
| 2 | Cam-1 Control | Cam-1 presets |
| 3 | Cam-2 Control | Cam-2S presets |
| 4 | Cam-3 Control | Cam-3 presets |
| 5 | Cam-4 Control | Cam-4 presets |
| 6 | Cam-1 Setup | Cam-1 presets save, D-pad, zoom, focus, speeds |
| 7 | Cam-2 Setup | Cam-2S presets save, zoom, focus (no D-pad) |
| 8 | Cam-3 Setup | Cam-3 build |
| 9 | Cam-4 Setup | Cam-4 build |
| 10 | Lighting | emergency OSC to B-Station |
| 11 | Testing Grounds | scratch |

---

## 4. Page 1 - Home

Row 0: `Home Page` (->p1, on-page indicator) | `Cam-1 Control` (->p2) | `Cam-2 Control` (->p3) | `Cam-3 Control` (->p4) | `Cam-4 Control` (->p5) | short_1 (PGM1, red tally) | short_2 (PGM2) | short_3 (PGM3)

Row 1: - | `Lighting controls` (->p10) | - | - | - | short_4 (PGM4) | short_5 (PGM5) | short_6 (PGM6)

Row 2: `Cam-1 Setup` (->p6) | `Cam-2 Setup` (->p7) | `Cam-3 Setup` (->p8) | `Cam-4 Setup` (->p9) | `AUTO` (ATEM auto, red while in transition) | short_1 (PVW1, green) | short_2 (PVW2) | short_3 (PVW3)

Row 3: `Connect Refresh` (disables all six device instances, waits 1 s, re-enables) | `Reboot Companio` (app restart) | - | `Lower 3rd` (2-step OBS scene toggle: Lower 3rd / Atem) | `CUT` | short_4 (PVW4) | short_5 (PVW5) | short_6 (PVW6)

Interpretation: left 5 columns are navigation and switching, right 3 columns are a permanent tally strip (top half program, bottom half preview). Program and preview are separate button banks, not toggles.

---

## 5. Pages 2-5 - camera control

Layout is identical on all four pages except the two named presets.

| Cell | Cam-1 (p2) | Cam-2 (p3, Cam-2S) | Cam-3 (p4) | Cam-4 (p5) |
|---|---|---|---|---|
| [1,0] | 1 | 1 | 1 | 1 |
| [1,1] | 2 | 2 | 2 | 2 |
| [1,2] | 3 | 3 | 3 | 3 |
| [2,0] | 4 | 4 | 4 | 4 |
| [2,1] | 5 | 5 | 5 | 5 |
| [2,2] | 6 | 6 | 6 | 6 |
| [3,0] | 7 | 7 | 7 | 7 |
| [3,1] | `Full Stage` (P8) | `Full Stage` (**P1**) | `Full Stage` (P8) | `Full Stage` (P8) |
| [3,2] | `Sermon` (P9) | `Crowd Wide` (**P2**) | `Sermon` (P9) | `Sermon` (P9) |

Shared on each page: row 0 = navigation (with on-page indicator), program tally on rows 0-1 right, preview on rows 2-3 right, `AUTO` at [2,4], `Lower 3rd` at [3,3], `CUT` at [3,4].

Note the Cam-2S anomaly: the control page recalls presets 1 and 2 for the two named shots, while its setup page stores those shots into 8 and 9.

---

## 6. Page 6 - Cam-1 Setup (template for 8 and 9)

| Cell | Text | Action |
|---|---|---|
| [0,1] | Cam-1 Setup | on-page indicator |
| [0,3] | `Zoom Speed $(Cam-1:zoom_speed)` | cycles 1 -> 3 -> 5 -> 7 |
| [0,4] | In | zoom in while held, stop on release |
| [0,5] [0,6] [0,7] | (no text) | PT up_left / up / up_right, stop on release |
| [1,0] [1,1] [1,2] | Save Preset 1/2/3 | savePset 1/2/3 |
| [1,4] | Out | zoom out while held |
| [1,5] | (no text) | PT left |
| [1,6] | HOME | PT home |
| [1,7] | (no text) | PT right |
| [2,0] [2,1] [2,2] | Save Preset 4/5/6 | savePset 4/5/6 |
| [2,4] | `Pan Speed $(Cam-1:pan_speed)` | cycles 1 -> 5 -> 10 -> 15 -> 20 |
| [2,5] [2,6] [2,7] | (no text) | PT down_left / down / down_right |
| [3,0] | Save Preset 7 | savePset 7 |
| [3,1] | Save Full Stage | savePset 8 |
| [3,2] | Save Sermon | savePset 9 |
| [3,4] | `Tilt Speed $(Cam-1:tilt_speed)` | cycles 1 -> 5 -> 10 -> 15 -> 18 |
| [3,5] | Focus In | focus near, stop on release |
| [3,6] | Focus Out | focus far, stop on release |
| [3,7] | `Focus Mode $(Cam-1:focus_mode)` | toggles focus mode, green when Auto |

Differences elsewhere:

- **Page 7 (Cam-2S)**: no D-pad and no pan/tilt speed buttons; only zoom, presets, focus. `Save Crowd Wide` writes preset 9. Also `Save\nFull Stage` writes 8.
- **Page 9 (Cam-4)**: same layout as page 6, but the labels are single-line ("Save Preset 1") instead of three-line.
- Pages 8 and 9 are byte-for-byte the same actions as page 6 with the instance swapped.

The D-pad buttons send a relative move that carries its own pan/tilt speed (9) in the action options, and `stop` on release. The `Pan Speed` / `Tilt Speed` buttons change module variables that are shown in the labels; whether those variables also affect the D-pad has to be confirmed on live cameras, because the action options override them.

---

## 7. Page 10 - Lighting

Row 0: `Home Page` | `These 2 Buttons Are Only For Emergency` | (cols 6-7) OSC `/B1/On`, `/B2/On`

Row 1: `Button Control On` (OSC `/button/on`) | `/B3/On`, `/B4/On`

Row 2: `Button Control Off` (OSC `/button/off`) | `/B5/On`, `/B6/On`

Row 3: `Test` (->p11)

Target: B-Station at 192.168.10.210 via the `osc` connection (UDP out to .210:8000).

---

## 8. Page 11 - Testing Grounds

Three empty button shells; only [0,2] has an action, a Cam-4 preset recall with the value stored as the string `"1"`. Safe to ignore.

---

## 9. State and feedback conventions worth mirroring

| Indicator | Definition | Style |
|---|---|---|
| ATEM program | `Atem.program_bg` per input | red background #ff0000 |
| ATEM preview | `Atem.preview_bg` per input | green background #00ff00 |
| ATEM in transition | `Atem.inTransition` on AUTO and CUT | red #ff0000 while moving |
| On this page | `surface_on_page` | red #ff0000 |
| Focus mode | `Cam-N.focusMode = Auto` | green #00ff00 |
| OBS scene | `obs.sceneProgram` | red on "Lower 3rd", black on "Atem" |
| Connection health | `instance_custom_state` good/error | dark green / red |

The Windows app should use the same colour language: red program, green preview, amber/red while a transition is running.

---

## 10. Anomalies and questions for the team

1. **Cam-2S named presets disagree.** Control page: Full Stage = preset 1, Crowd Wide = preset 2. Setup page: Save Full Stage = 8, Save Crowd Wide = 9. The team confirms Cam-2S is static and only presets 8 and 9 are ever used, so the control page wiring is the error to fix in Companion (the app will recall 8 and 9).
2. **Pan/tilt speed step numbers skip 1** (steps 0, 2, 3, 4, 5), so the cycling values are 1, 5, 10, 15, 20 rather than the 1-5 usually intended. Intentional or leftover?
3. Tilt tops out at 18 while pan tops out at 20. Probably intentional (tilt is more sensitive), worth confirming.
4. Cam-4 setup labels are single-line; harmless but suggests that page was edited later.
5. "Reboot Companio" is truncated to fit the button; the action is a full Companion restart.
6. Companion is 4.2.2 while 5.1.3 is current. Any upgrade changes page layout, module versions and the export format - re-run this capture before and after.

---

## 11. What this means for the Windows port

- **Preset policy (decided):** the app recalls presets 1-9, exactly as the Stream Deck does, but only ever writes 20-39. `firstPreset = 20`.
- **Named shots:** the app should label 8 and 9 per camera ("Full Stage", "Sermon" / "Crowd Wide") rather than showing bare numbers, matching the Stream Deck.
- **Camera panel vocabulary:** D-pad with speeds, zoom in/out, focus near/far, focus-mode toggle, preset grid. Volunteers already know this layout, so the app should not invent a different one.
- **Speed feel:** the Stream Deck sends fixed speeds 9 for D-pad moves and separate zoom speeds 1/3/5/7. The app's joystick ramp should be compared against this at the booth so both feel the same.
- **Conflict avoidance:** because the Stream Deck can recall presets while the app is tracking, the app must detect external position changes (readback vs commanded) and pause tracking, as already planned.
- **Not in scope for the app:** lighting OSC, ProPresenter, B-Station, Lower 3rd scene switching, page navigation. CUT and AUTO are duplicated by the app on purpose (the ATEM accepts many clients).
- **Feedbacks to copy:** program red / preview green / transition flashing / focus-mode green gives the operator one consistent language across Stream Deck and the app.
