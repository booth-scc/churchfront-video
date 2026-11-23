# Coding standards

Review-time rules that need judgement. Mechanical rules belong in `windows/check.ps1` or CI, not here.

## Architecture

- `CueVideo.Core` holds policy and decisions. It does not use Avalonia types.
- The window is an adapter: it turns view visibility, dialogs and focus into plain values for Core, and renders what Core returns. Shortcut policy does not live in the window.

## Naming

- Name state after what it is, not where it appears.
- Pass adjacent booleans by name at call sites once there are two or more.

## Feedback

- Status text reports what happened. When a control cannot act (unsupported protocol, no address, no link), say why; never print success after a skipped command.

## Tests

- Test behaviour at a public seam; name tests as specifications.
- A policy table can be one theory. Wiring still needs its own check.
- Any shortcut, focus or dialog safety change needs either an Avalonia headless test or the manual checklist in `windows/README.md`.
