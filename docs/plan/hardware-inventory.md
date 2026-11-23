# Booth PC hardware inventory - 2026-11-20

Collected read-only from Windows (CIM/WMI, `nvidia-smi`, storage APIs). Nothing was changed.

## System

| Item | Value |
|---|---|
| Machine | MSI MS-7E06 - Z790 GAMING PLUS WIFI |
| BIOS | AMI H.70, 2024-09-26 |
| CPU | Intel Core i7-12700KF, 12 cores / 20 threads, 3.6 GHz base (socket U3E1) |
| RAM | 32 GB - 2 x 16 GB G.Skill F5-6000J3636F16G, **running at 4800 MT/s** (kit is 6000) |
| GPU | NVIDIA GeForce RTX 5060, 8 GB (8151 MiB), driver 610.88, 30 C idle, 12.8 W idle / 145 W limit |
| OS | Windows 11 Pro 25H2, build 26200, installed 2025-12-10 |
| Uptime at audit | 10.4 h |
| Power plan | Balanced |

## Storage

| Device | Size | Type | Health |
|---|---|---|---|
| Samsung SSD 990 EVO Plus 4TB | 3726 GB | NVMe SSD | Healthy |
| Generic Flash Disk | 0 GB | USB | Healthy (appears to be a small card/flash reader) |

`C:` is the 4 TB drive: **3.53 TB free**. Plenty of room for recordings, models and the app.

## Displays

| Device | Resolution | Role |
|---|---|---|
| `\\.\DISPLAY2` | 1920x1080 @ 60 Hz | primary |
| `\\.\DISPLAY1` | 1280x720 | secondary, via an "LKV HDbitT" HDMI extender/receiver |
| Dell S2415H | 1920x1080 panel (EDID) | the primary monitor |

The 720p output matters: the operator window must stay usable when mirrored to the extender.

## Network

| Item | Value |
|---|---|
| NIC | Intel Ethernet Controller I226-V, 1 Gbps, MAC 34-5A-60-70-EE-0D |
| Address | 192.168.10.101/24, gateway 192.168.10.1 |
| Wi-Fi | Intel Wi-Fi 6E AX211 (present, not carrying the booth traffic) |

1 Gbps is comfortable for four NDI|HX camera streams plus OBS program NDI; no 2.5G/10G needed.

## Capture and audio

| Device | Notes |
|---|---|
| Blackmagic DeckLink Mini Recorder 4K | single SDI input, Desktop Video 15.3, owned by OBS |
| Blackmagic Audio | DeckLink embedded SDI audio - the only audio path used by the stream |
| BEHRINGER UMC 204HD 192k | USB interface, currently not mixed into OBS (both global audio sources muted) |
| Realtek HD Audio, NVIDIA HDMI audio, NVIDIA virtual audio | system devices |
| NDI Webcam Audio/Video | NDI Tools virtual devices |

## Other devices

- Sentinel HASP / HL USB keys (software licensing dongles - leave in place)
- Brother MFC-L2690DW (printer/scanner)
- Elgato Stream Deck XL on USB

## What this means for Churchfront Video

| Requirement | Verdict |
|---|---|
| CPU headroom for 4 NDI decodes + tracking | Fine: 20 threads, OBS uses NVENC, pose inference is 5-10 Hz |
| GPU for DirectML pose | Fine: 8 GB VRAM, 145 W budget, 30 C idle |
| Disk for models and recordings | Fine: 3.5 TB free |
| Network | Fine: 1 Gbps wired |
| Displays | Design must hold at 1280x720 as well as 1920x1080 |
| Capture card | Single input, OBS owns it - no second capture for the app, as planned |

## Notes worth raising (no action taken)

1. **RAM is at 4800 MT/s**, not the kit's 6000 - XMP/EXPO is off in BIOS. Turning it on is a small win for tracking maths and general responsiveness, but it is a BIOS change; do it in a maintenance window and re-run a stream test.
2. **Power plan is Balanced.** High Performance would keep clocks up between takes; optional, and it raises idle power/heat in a booth.
3. **TPM is not reported** by WMI (likely disabled in BIOS). Windows 11 is installed and running, so this only matters if BitLocker or a TPM-bound feature is ever wanted.
4. **Second display is 720p through an HDMI extender.** Any layout that needs 1440 px of height will not fit there; the operator window is designed at 1500x940, so on the extender it must scale down rather than clip.
5. **DeckLink Mini Recorder 4K is a single-input card.** Confirms there is no second SDI path for a multiview capture; per-camera NDI remains the only picture source for Next and tiles.
