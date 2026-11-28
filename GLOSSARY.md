# Glossary

Terms used in issues, docs and code. Add an entry when a term causes a question.

## Screens

- **Operator** — the screen volunteers run: Next/Live, source tiles, and the camera controls for the camera on Next.
- **Engineer** — the setup and status screen: NDI feed mapping, camera addresses, switcher address and connection tests.
- **operator status** — the camera-on-Next area of the Operator screen: the camera chip and the notice under it.

## Cameras

- **camera on Next** — the configured camera whose ATEM input matches the preview bus. Every camera control acts on it.
- **supported protocol** — a protocol the Windows port can speak: VISCA over IP on UDP 52381 (`sonyUDP`). `rawTCP` and `canonXC` are macOS-only and are refused.

## Tracking

- **tracking** — the app following a person in the camera on Next picture and framing the shot itself.
- **override** — the operator taking manual control while tracking is on; tracking pauses rather than fight the input.
