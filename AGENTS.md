# Churchfront Video

Church production control app: an ATEM switcher surface with PTZ camera control, NDI previews, auto-tracking and stage scan. The macOS app is Swift/SwiftUI in `Sources/`; the Windows port is C#/Avalonia in `windows/`.

**This fork works in `windows/` only.** `Sources/` and `Tests/` are upstream macOS (`Churchfront/churchfront-video`) and stay untouched. Windows layout, build, checks and demo: `windows/README.md`. Windows work order: see #36. Windows port plan: `docs/plan/churchfront-windows-port.md`. Domain terms: `GLOSSARY.md`. List tracked sources with `git ls-files windows`; `bin/` and `obj/` are build output.

## Checks

Run `powershell -ExecutionPolicy Bypass -File windows\check.ps1` before committing. It builds the Windows solution, checks C# formatting and runs the test suites. After it passes, commit on `main` and push `origin/main`; this fork does not use feature branches or PRs. Close the ticket with a summary comment once the Windows checks pass (see `docs/agents/issue-tracker.md`).

## Agent skills

### Issue tracker

Issues live in the fork's GitHub Issues (`booth-scc/churchfront-video`), handled with `gh`. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical roles use their default label strings. See `docs/agents/triage-labels.md`.

### Coding standards

Judgement calls for reviewers live in `CODING_STANDARDS.md`.
