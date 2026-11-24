# Coding standards

Review-time rules that need judgement. Mechanical rules belong in `windows/check.ps1` or CI, not here.

## Architecture

- `CueVideo.Core` holds policy and decisions. It does not use Avalonia types.
- The window is an adapter: it turns view visibility, dialogs and focus into plain values for Core, and renders what Core returns. Shortcut policy does not live in the window.
- A background producer (file poll, socket loop, timer) never touches the window, camera links or the switcher; it hands the change to the owner's thread, and ignores work that arrives after disposal.
- A client seam lives with the client project (`ISwitcher` in `CueVideo.Atem`); a Core adapter that forwards every member is a middle man.
- After an adapter rebuilds controls from config, re-apply the Core snapshot immediately; don't wait for the next device packet.

## Naming

- Name state after what it is, not where it appears.
- Pass adjacent booleans by name at call sites once there are two or more.

## Feedback

- Status text reports what happened. When a control cannot act (unsupported protocol, no address, no link), say why; never print success after a skipped command.

## Tests

- Test behaviour at a public seam; name tests as specifications.
- A policy table can be one theory. Wiring still needs its own check.
- A test named for a configuration sets it away from the default, or it proves nothing.
- A round-trip test saves before it reloads, and compares both directions: dropping a key fails, and so does adding an unexpected one.
- Any shortcut, focus or dialog safety change needs either an Avalonia headless test or the manual checklist in `windows/README.md`.
