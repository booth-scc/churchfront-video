# Coding standards

Review-time rules that need judgement. Mechanical rules belong in `windows/check.ps1` or CI, not here.

## Architecture

- `CueVideo.Core` holds policy and decisions. It does not use Avalonia types.
- The window is an adapter: it turns view visibility, dialogs and focus into plain values for Core, and renders what Core returns. Shortcut policy does not live in the window.
- A background producer (file poll, socket loop, timer) never touches the window, camera links or the switcher; it hands the change to the owner's thread, and ignores work that arrives after disposal.
- A shared feed guards each subscriber separately, retries a transient client failure while it has subscribers, and releases without joining its thread on the UI thread.
- A retain/release pair sets the stop deadline under the same lock as the last release.
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
- A round-trip test compares the saved document in both directions — dropping a key fails, and so does adding an unexpected one — and reloads what it saved.
- Wait for a background thread with `TestWait.Until` in `CueVideo.TestSupport`; pass `Dispatcher.UIThread.RunJobs` for headless Avalonia tests. Do not copy a private wait loop.
- A demo assertion observes the value that changed (a connection dropping to zero), not just two unchanged values matching.
- Any shortcut, focus or dialog safety change needs either an Avalonia headless test or the manual checklist in `windows/README.md`.
