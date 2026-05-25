# Roadmap - pending work only

**Last updated:** 2026-05-25
**Current baseline:** v0.9.0 working tree, pending commit from `c945e0d`.
**Verification baseline:** `dotnet test QuotaGlass.sln` passes with .NET SDK 9.0.314: 87 passed, 0 failed.

This file is the executable TODO. Completed items live in [CHANGELOG.md](CHANGELOG.md) by release and are intentionally not duplicated here. Background evidence lives in the research dossiers:

- [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md) - Pass 1 audit.
- [RESEARCH_PASS_2.md](RESEARCH_PASS_2.md) - Pass 2 audit.
- [RESEARCH_PASS_3.md](RESEARCH_PASS_3.md) - post-v0.1.0 audit.
- [RESEARCH_PASS_4.md](RESEARCH_PASS_4.md) - post-v0.4.0 audit.
- [RESEARCH_PASS_5.md](RESEARCH_PASS_5.md) - post-v0.8.0 audit and v0.9/v0.10 queue.
- [docs/research.md](docs/research.md) - original scaffold dossier.
- [docs/extension-integration.md](docs/extension-integration.md) - wire schema spec.
- [docs/bridge-integration.md](docs/bridge-integration.md) - extension-side drop-in code.

Resolved decisions are captured in [CHANGELOG.md](CHANGELOG.md): self-hosted updater, AUMID, Win10 1809 minimum, unsigned binaries, no telemetry, no MSIX, no Tauri/Rainmeter/Avalonia pivot for the current product.

---

## Blocked / Needs Human Runtime

- [ ] **R5-P0-02 / R5-N6 - Verify Claude Code OAuth refresh endpoint.** Requires a real Claude Code install with OAuth credentials and network capture or CLI source confirmation. Until verified, keep the limitation documented: cached Claude Code OAuth access tokens may stop refreshing after expiry; users can rerun `claude login`.
- [ ] **P3 - N-20 screenshots.** Needs the widget running against representative data so `assets/screenshots/` can show real cards, settings, log panel, high contrast, and setup states.
- [ ] **R5-N7 - CI-triggering PR.** Requires GitHub PR workflow execution rather than direct local commit/push.

Blocked items do not stop autonomous execution; continue with the next unblocked task.

---

## Phase 8 - v0.10.0 - localization proof + scheduler tests + cleanup

- [ ] **R5-N1 - XAML to `Strings` proof-of-concept.** Wire one visible Setup/Card/Settings string through `Resources/Strings.cs` so the localization scaffold is exercised end-to-end before a full RESX migration.
- [ ] **R5-N2 - AlarmScheduler dedup unit tests.** Add focused tests for fire-once behavior, snooze suppression, Focus Assist suppression, and U3/R3 interaction. Keep the production API small; introduce interfaces only if required by tests.
- [ ] **R5-P1-03 - Rename CLSID near-collision.** Separate the toast activator CLSID from the Inno AppId GUID family so future maintenance does not confuse `...D2A2` with `...D2A1`. Update installer and registration docs in the same change.
- [ ] **MainWindow.xaml.cs split.** Extract helper classes for tray wiring, update checks, and bucket context menu once tests are in place.

---

## Phase 9 - deferred product bets

- [ ] **P3 - L-10 provider plugin contract.** Deferred until a real second-provider use case exists.
- [ ] **R-Log-02 - Correlation IDs across NMH and Widget.** Adds value once multi-extension fan-in or multi-NMH sessions exist.
- [ ] **R2-P2-01 - Working-day Pace integration.** Power-user feature based on the Zrnik `Pace.cs` pattern.
- [ ] **L-12 - Native messaging companion to keep extension service worker alive.** Mostly handled by the existing persistent-port 25 s ping; revisit only if service-worker death incidents recur.
- [ ] **L-03 / UC-01 / UC-02 - Win11 Widgets board, Avalonia port, WinUI 3 port.** Under consideration; no current demand.

---

## Next Autonomous Pick

Start with **R5-N1** unless a real Claude Code OAuth environment is available. R5-P0-02 is higher severity, but it is blocked on live validation that cannot be synthesized from this repository.
