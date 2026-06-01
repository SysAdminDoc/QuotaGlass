# Roadmap - pending work only

**Last updated:** 2026-06-01
**Current baseline:** v0.10.0 plus installer CLSID fixes on `main`.
**Last recorded verification:** `dotnet test QuotaGlass.sln --no-restore` with .NET SDK 9.0.314: 101 passed, 0 failed.

This file is the executable TODO. Completed items live in [COMPLETED.md](COMPLETED.md) and [CHANGELOG.md](CHANGELOG.md). Research synthesis lives in [RESEARCH_REPORT.md](RESEARCH_REPORT.md); historical audit dossiers are archived under `docs/archive/research/`.

## Reference Docs

- [COMPLETED.md](COMPLETED.md) - shipped baseline by feature area.
- [RESEARCH_REPORT.md](RESEARCH_REPORT.md) - current research synthesis and decisions.
- [docs/archive/research/RESEARCH_FEATURE_PLAN.md](docs/archive/research/RESEARCH_FEATURE_PLAN.md) - Pass 1 audit.
- [docs/archive/research/RESEARCH_PASS_2.md](docs/archive/research/RESEARCH_PASS_2.md) - Pass 2 audit.
- [docs/archive/research/RESEARCH_PASS_3.md](docs/archive/research/RESEARCH_PASS_3.md) - post-v0.1.0 audit.
- [docs/archive/research/RESEARCH_PASS_4.md](docs/archive/research/RESEARCH_PASS_4.md) - post-v0.4.0 audit.
- [docs/archive/research/RESEARCH_PASS_5.md](docs/archive/research/RESEARCH_PASS_5.md) - post-v0.8.0 audit and v0.9/v0.10 queue.
- [docs/archive/research/RESEARCH_PASS_6.md](docs/archive/research/RESEARCH_PASS_6.md) - production hardening audit.
- [docs/archive/research/RESEARCH_PASS_7.md](docs/archive/research/RESEARCH_PASS_7.md) - premium UI/UX polish audit.
- [docs/archive/research/research.md](docs/archive/research/research.md) - original scaffold dossier.
- [docs/extension-integration.md](docs/extension-integration.md) - wire schema spec.
- [docs/bridge-integration.md](docs/bridge-integration.md) - extension-side drop-in code.

Resolved decisions are summarized in [RESEARCH_REPORT.md](RESEARCH_REPORT.md) and captured historically in [CHANGELOG.md](CHANGELOG.md): self-hosted updater, AUMID, Win10 1809 minimum, unsigned binaries, no telemetry, no MSIX, no Tauri/Rainmeter/Avalonia pivot for the current product.

---

## Blocked / Needs Human Runtime

- [ ] **R5-P0-02 / R5-N6 - Verify Claude Code OAuth refresh endpoint.** Requires a real Claude Code install with OAuth credentials and network capture or CLI source confirmation. Until verified, keep the limitation documented: cached Claude Code OAuth access tokens may stop refreshing after expiry; users can rerun `claude login`.
- [ ] **P3 - N-20 screenshots.** Needs the widget running against representative data so `assets/screenshots/` can show real cards, settings, log panel, high contrast, and setup states.
- [ ] **R5-N7 - CI-triggering PR.** Requires GitHub PR workflow execution rather than direct local commit/push.

Blocked items do not stop autonomous execution when unblocked tasks exist.

---

## Phase 9 - Deferred Product Bets

- [ ] **P3 - L-10 provider plugin contract.** Deferred until a real second-provider use case exists.
- [ ] **R-Log-02 - Correlation IDs across NMH and Widget.** Adds value once multi-extension fan-in or multi-NMH sessions exist.
- [ ] **R2-P2-01 - Working-day Pace integration.** Power-user feature based on the Zrnik `Pace.cs` pattern.
- [ ] **L-12 - Native messaging companion to keep extension service worker alive.** Mostly handled by the existing persistent-port 25 s ping; revisit only if service-worker death incidents recur.
- [ ] **L-03 / UC-01 / UC-02 - Win11 Widgets board, Avalonia port, WinUI 3 port.** Under consideration; no current demand.

---

## Next Autonomous Pick

No unblocked local implementation tasks remain after Pass 6 and Pass 7. Resume with **R5-P0-02** when a real Claude Code OAuth environment is available, or promote one deferred product bet above if demand changes.
