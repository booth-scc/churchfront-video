# Churchfront Video

Church production control app: an ATEM switcher surface with PTZ camera control, NDI previews, auto-tracking and stage scan. The macOS app is Swift/SwiftUI in `Sources/`; the Windows port is C#/Avalonia in `windows/`.

**This fork works in `windows/` only.** `Sources/` and `Tests/` are upstream macOS and stay untouched. Windows work order: see #36.

## Checks

Run `powershell -ExecutionPolicy Bypass -File windows\check.ps1` before committing. It builds the Windows solution and runs the test suite.

## Agent skills

### Issue tracker

Issues live in the fork's GitHub Issues (`booth-scc/churchfront-video`), handled with `gh`. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical roles use their default label strings. See `docs/agents/triage-labels.md`.

### Coding standards

Judgement calls for reviewers live in `CODING_STANDARDS.md`. Mechanical checks belong in `windows/check.ps1` or CI.
