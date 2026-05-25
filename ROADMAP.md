# Roadmap — pending work only

**Last updated:** 2026-05-25 · **Head:** `27108e2` · **Current shipped:** v0.4.0.

This file is the **executable** TODO. Completed items live in [CHANGELOG.md](CHANGELOG.md) by release; do not duplicate them here. Background and evidence live in the research dossiers:

- [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md) — Pass 1 audit (positioning, schemas, F-A* / F-N*).
- [RESEARCH_PASS_2.md](RESEARCH_PASS_2.md) — Pass 2 audit (R2-P0-* / R2-P1-* / Pass 1 corrections).
- [RESEARCH_PASS_3.md](RESEARCH_PASS_3.md) — Pass 3 post-ship audit (5 shipped bugs).
- [RESEARCH_PASS_4.md](RESEARCH_PASS_4.md) — Pass 4 post-v0.4.0 audit (6 bugs in v0.1.1..v0.4.0 work, v0.5+ queue).
- [docs/research.md](docs/research.md) — original scaffold dossier.
- [docs/extension-integration.md](docs/extension-integration.md) — wire schema spec.
- [docs/bridge-integration.md](docs/bridge-integration.md) — extension-side drop-in code.

Resolved open questions (defaulted by the autonomous agent, see CHANGELOG headers): self-hosted updater, AUMID, Win10 1809 minimum, unsigned binaries, etc.

---

## Phase 4 — v0.5.0 — F-N1 fixes + Mica regression + perf ✅ (2026-05-25)

Surfaced by [RESEARCH_PASS_4.md](RESEARCH_PASS_4.md). All P0/P1 items + 9 quick wins shipped this session. See [CHANGELOG.md](CHANGELOG.md) for per-item details.

- [x] **R4-P0-01..04** — F-N1 endpoint / token-shape / OAuth refresh / zero token burn.
- [x] **R4-P0-03** — Mica + ThemeService coordination.
- [x] **R4-P1-01** — HistoryStore debounce.
- [x] **R4-P1-02** — Snapshot multi-source merge (`snapshot.json` + `snapshot.local-creds.json`).
- [x] **R4-N1** — OAuth refresh-token rotation.
- [x] **R4-N4** — `--poll-credentials` Scheduled Task auto-start.
- [x] **R4-Q-01/03/04/05/06/07/08/09/11** — quick wins.

---

## Phase 5 — v0.6.0 — Toast actions + schema v2 + tests ✅ (2026-05-25)

Shipped this session. See [CHANGELOG.md](CHANGELOG.md) for per-item details.

- [x] **R4-N2 / L-04** — Toast actions via hand-rolled COM activator. ([Services/ToastActivator.cs](src/QuotaGlass.Widget/Services/ToastActivator.cs), [installer/quotaglass.iss](installer/quotaglass.iss))
- [x] **R4-N3** — Schema v2 bundles `state.history` in the wire envelope.
- [x] **R4-N7** — `XmlEscape` extracted to Shared + 6 unit tests covering all 5 XML entities.
- [x] **HistoryStore + FiredRulesStore** moved into Shared + 10 unit tests (cap, dedupe, prune).
- [x] **Diagnostics.Collect** zip integration test (redacts orgId/accountId/WAV paths).

### Carried into v0.7

- [ ] **R4-N5 / R3-P2-01 full** — Multi-account columns full UI (needs real data first).
- [ ] **Architecture refactor** — MainWindow.xaml UserControl extraction (defer until CI has a chance to validate every PR).
- [ ] **Settings panel sub-sections** — Alarms / Display / Integration / Advanced.

---

## Phase 6 — v0.7.0+

- [ ] **P2 — R4-N5 / R3-P2-01 full** — Multi-account columns within a provider.
- [ ] **P2 — Architecture refactor** — Extract `Views/SetupCard.xaml`, `SettingsPanel.xaml`, `CalendarPanel.xaml`, `LogPanel.xaml` as `UserControl`s.
- [ ] **P2 — Settings panel sub-sections** — Group 14+ controls into expandable "Alarms" / "Display" / "Integration" / "Advanced".
- [ ] **P2 — R4-N6 / L-06** — Named-pipe NMH↔Widget transport (`\\.\pipe\QuotaGlass.Snapshot`). Drops snapshot→render latency from ~270 ms to <10 ms; falls back to FileSystemWatcher when no listener.
- [ ] **P2 — R4-N8** — High-contrast theme + "Follow system theme" mode.
  - Touches: new `Theme/HighContrast.xaml`, [Services/ThemeService.cs](src/QuotaGlass.Widget/Services/ThemeService.cs).
- [ ] **P3 — R4-N9 / R3-P3-01** — Localization scaffold (`Resources.resx` + `CurrentUICulture`).
- [ ] **P3 — L-10** — Provider plugin contract (deferred until a real second-provider use case lands).
- [ ] **P3 — N-20** — Manual screenshots for `assets/screenshots/`. Needs an actual runtime to capture.
- [ ] **P3 — L-03 / UC-01 / UC-02** — Win11 Widgets board integration / Avalonia port / WinUI 3 port — under-consideration; no demand yet.

---

## Carry-forward (older but still open)

- [ ] **R-Log-02** — Correlation IDs across NMH ↔ Widget. Adds value only once multi-extension fan-in lands; deferred indefinitely.
- [ ] **R2-P2-01** — Working-day Pace integration (Zrnik `Pace.cs` pattern). Power-user feature; defer until pace path proves out.
- [ ] **L-12** — Native messaging companion to keep extension SW alive. Mostly handled by F-A4's 25 s ping; revisit if SW-death incidents recur.

---

## Rejected (decisions captured — do not re-open)

- **R-01..R-08, R2-NG-01..R2-NG-04** — see Pass 1 / Pass 2 dossiers for full rationale. Recap: no Rainmeter, no Tauri port, no direct Chromium cookie reads, no WPF re-implementation, no pill backdrops, no paid tier, no confetti, no GPL switch, no Jira/Toggl integrations, no MSIX, no verbatim CredentialStore port, no telemetry.

---

## Themes covered (post-v0.4.0 snapshot)

| Category | Coverage |
|---|---|
| UX | NX-04..NX-09, L-02, L-08, R3-P2-05/06/07, R4-Q-06/09 |
| Reliability | R1 ladder, HICON, FiredRulesStore, Mutex, R4-P0-01..04, R4-P1-01/02 |
| Security | Toolkit-CVE removal, JSON depth cap, origin enforcement, URL scheme guard, F-N7 env-var pass |
| Integrations | F-A1, F-A2, F-A4, F-N1 (broken — R4-P0-01..04 to fix), F-N7, L-10 |
| Accessibility | F-A19, NX-07, UX-Acc-01..03 (closed), R4-N8 (open) |
| Performance | F-A17, R4-P1-01, R4-N6 |
| Distribution | F-N10, self-hosted updater, Inno installer, GH Releases workflow + CI |
| Testing | 37 method-level tests across 6 fixtures (AtomicJsonFile, SchemaVersion, SnapshotSchema, Ladder, Credential, Plan, Anomaly) |
| Docs | extension-integration, bridge-integration, SECURITY, CONTRIBUTING, four research dossiers |
| Theme | Mocha (Catppuccin dark), Latte (Catppuccin light), Mica composition, R4-N8 high-contrast (open) |
