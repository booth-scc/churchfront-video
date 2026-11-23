# Coding standards

Review-time rules that need judgement. Mechanical rules belong in `windows/check.ps1` or CI, not here.

## Architecture

- `CueVideo.Core` holds policy and decisions. It does not use Avalonia types.
- The window is an adapter: it turns view visibility, dialogs and focus into plain values for Core, and renders what Core returns. Shortcut policy does not live in the window.

## Naming

- Name state after what it is, not where it appears.
- Pass adjacent booleans by name at call sites once there are two or more.

## Tests

- Test behaviour at a public seam; name tests as specifications.
- A policy table can be one theory. Wiring still needs its own check.
- Any shortcut, focus or dialog safety change needs either an Avalonia headless test or the manual checklist in `windows/README.md`.

## Review

- Report documented-standard breaches as violations; report the smell baseline as judgement calls.
- Prefer a deterministic check over a written rule whenever the rule is mechanical.
