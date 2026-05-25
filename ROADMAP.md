# Roadmap — single source of truth

**Last updated:** 2026-05-25 · **Current:** v0.1.0-dev (commit `b9061b7` + research dossiers).

This file is the **executable** TODO. It merges the original three planning files. Background and evidence still live in:

- [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md) — Pass 1 audit (positioning, schemas, F-A*/F-N*).
- [RESEARCH_PASS_2.md](RESEARCH_PASS_2.md) — Pass 2 audit (R2-P0-*, R2-P1-*, Pass 1 corrections).
- [docs/research.md](docs/research.md) — original scaffold dossier (sections corrected by Pass 2).

Resolved open questions (defaulted by the autonomous agent, 2026-05-25):

- **Updater:** self-hosted GitHub-Releases + PowerShell self-replace (Zrnik pattern), not Velopack. Matches "small + auditable" ethos.
- **Toast actions** (Snooze/Open): v0.2; v0.1 ships text+sound only.
- **OS minimum:** Win10 1809 (build 17763); revisit at v0.3.
- **AppUserModelID:** `com.sysadmindoc.QuotaGlass.Widget`.
- **AI-Usage_Tracker manifest `"key"`:** add now; one-time-only break for pre-release extension users (very small population).
- **Installer post-install:** auto-launch widget; show first-run "QuotaGlass is in your tray" toast.
- **Code signing:** unsigned for v0.1.x; SmartScreen workaround noted in README.

---

## Shipped

- [x] v0.1.0-dev scaffold (`b9061b7`) — three-project .NET 9 solution, NMH binary, WPF widget skeleton, MIT, branch protection, MEMORY.md index entry.
- [x] `docs/research.md` — original landscape research (Pass 2 correction applied below).
- [x] [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md) — Pass 1 deep audit.
- [x] [RESEARCH_PASS_2.md](RESEARCH_PASS_2.md) — Pass 2 deep audit.

---

## Phase 0 — Unblock v0.1.0 (must land before any release)

### Batch 1 — Quick-win corrections ✅

- [x] **F-A3** — Firefox extension ID typo fixed.
- [x] **F-A7** — Title-bar `×` hides instead of quits.
- [x] **F-A18** — Atomic write fsync before rename.
- [x] **F-A20** — README "Install" placeholder replaced with shipping-status callout.
- [x] **F-A21** — `docs/research.md` §5 corrected (Windows competitors exist; audio doc fixed).
- [x] **F-A17** — `BucketViewModel.TickCountdown` caches formatted string; only INPC on change.
- [x] **F-A13** — NMH ack payload includes `nmhVersion`/`schemaMin`/`schemaMax`/`serverTime`.

### Batch 2 — Schema + integration contract

- [ ] **F-N9** — Author `docs/extension-integration.md` schema spec (canonical contract).
- [ ] **F-A1** — Rewrite `BucketSnapshot.cs` to mirror extension's actual `state` envelope (`fetchedAtISO`, `providers.{claude,codex}.{ok,source,orgId,plan,buckets[]}`, `bucket.{id,kind,model,label,percentUsed,resetISO,rawResetText}`).
- [ ] **F-A5** — Reconcile buckets by `Bucket.Id` not `Provider/Label` (rolls in with F-A1).
- [ ] **F-A12** — Schema versioning + migration scaffold (`Shared/SchemaVersion.cs`).
- [ ] **F-A13** — NMH ack payload includes `nmhVersion`, `schemaMin`, `schemaMax`, `serverTime`.
- [ ] **R2-P1-02** — JSON `MaxDepth = 16` on `SnapshotJsonContext`; per-field length checks.
- [ ] **F-A14** — Origin allow-list enforcement in `MessagePump`.

### Batch 3 — Toast + TopMost (alarm UX foundation)

- [ ] **R2-P0-01** — Drop `Microsoft.Toolkit.Uwp.Notifications` package; remove transitive `System.Drawing.Common 4.7.0` (GHSA-rxg9-xrhp-64gj). Write `Services/ToastService.cs` on raw `Windows.UI.Notifications`.
- [ ] **R2-P0-02** — `Services/TopMostEnforcer.cs` with WinEvent `EVENT_SYSTEM_FOREGROUND` hook on dedicated STA thread.
- [ ] **R2-P0-03** — Custom audio via `System.Media.SoundPlayer.Play()` + `<audio silent="true"/>` in toast XML. **Authoritative finding: `<audio src="file:///">` is silently ignored.**
- [ ] **N-12** — Toast notification adapter (rolls in with R2-P0-01).
- [ ] **N-13** — Alarm-ladder scheduler (24/12/6/3/1h, 30/15/5m, at-reset; configurable; fire-once idempotency `<provider>-<bucket>-<tier>-<resetISO>`).
- [ ] **N-14** — Zero-state R3 toast (bucket flips to `percentUsed >= 100`).

### Batch 4 — Widget polish

- [ ] **F-N8** — `--inject-fake-snapshot` dev mode (writes deterministic snapshot.json for solo widget dev).
- [ ] **F-N6** — Click bucket card → open analytics page in default browser via `Process.Start(url) { UseShellExecute = true }`.
- [ ] **F-A9** — Stale-snapshot visual state (greyed ring + colored status when `now - ts > 2× refresh interval`).
- [ ] **F-A19** — Catppuccin contrast fix: `Brush.Card.MutedText` from `Overlay1 #7F849C` → `Overlay0 #6C7086` for WCAG AA.
- [ ] **R2-P1-03** — Pace footer (`BucketViewModel.PaceLabel` derived from snapshot history).

### Batch 5 — Tray + first-run

- [ ] **F-N4** — System tray icon with right-click menu (Show/Hide/Refresh/Quit). `H.NotifyIcon.Wpf` package.
- [ ] **F-N3** — Setup Checklist card in widget when snapshot is missing/stale > 24h: 3 steps (extension installed, NMH registered, first snapshot received).

---

## Phase 1 — v0.1.0 ship

### Batch 6 — Settings

- [ ] **N-15** — Embedded settings panel (expand-down, not separate window).
- [ ] **N-16** — Settings persistence at `%LOCALAPPDATA%\QuotaGlass\settings.json` (atomic write).

### Batch 7 — Cross-repo bridge

- [ ] **F-A2** — Add `"key"` field to `AI-Usage_Tracker/manifests/chrome.json`; hardcode resulting Chrome ID in `HostRegistrar.ChromeExtensionIds`.
- [ ] **F-A4** — Write `AI-Usage_Tracker/src/lib/bridge.js` with persistent port, reconnect-on-disconnect, 25s ping. Add `"nativeMessaging"` to both manifests. Wire from `background.js` after `mergeSnapshot`.

### Batch 8 — Distribution

- [ ] **F-N10** — Add `win-arm64` to `RuntimeIdentifiers` in both csprojs.
- [ ] **R2-P1-08** — Register Start Menu shortcut with `System.AppUserModel.ID = com.sysadmindoc.QuotaGlass.Widget`. Use same AppId in `ToastNotificationManager.CreateToastNotifier`.
- [ ] **R2-P1-06** — Self-hosted updater (`Services/UpdateChecker.cs`) — GitHub Releases API + PowerShell self-replace script (Zrnik pattern).
- [ ] **N-17** — Inno Setup installer (`installer/quotaglass.iss`) that installs to `%LOCALAPPDATA%\Programs\QuotaGlass\`, runs `--register`, drops Start Menu shortcut with AUMID, autostarts widget on login, supports x64+arm64.
- [ ] **N-18** — GitHub Release workflow (`.github/workflows/release.yml`, `workflow_dispatch`, multi-arch build + sign-skip + Inno pack + GH release upload).

### Batch 9 — Logging + observability

- [ ] **F-A10** — Log rotation: delete `nmh-{date}.log` older than 14 days; size-cap individual files at 10 MB.
- [ ] **R-Rec-02** — `--purge` NMH flag wipes `%LOCALAPPDATA%\QuotaGlass\*`.
- [ ] **R-Log-03** — `Services/WidgetLogger.cs` mirroring NMH logger pattern; daily file rotation.
- [ ] **R-Log-02** — 4-char correlation ID per inbound NMH frame; propagate into snapshot.json `lastRequestId`.

### Batch 10 — Tests + final docs

- [ ] **F-A16** — `test/QuotaGlass.Tests/` xUnit project. 8 initial tests covering AtomicJsonFile, MessagePump framing, BucketViewModel countdown, RadialRing math, HostRegistrar manifest, JSON MaxDepth, origin enforcement, schema versioning.
- [ ] **N-19** — Real README install steps.
- [ ] **N-20** — Hero + popup + toast screenshots in `assets/screenshots/`, DPI-aware capture.

---

## Phase 2 — v0.2.0 polish + true differentiator

- [ ] **F-N1** — Direct credential reading (`%USERPROFILE%\.claude\.credentials.json`, `.codex\auth.json`, `.hermes\auth.json`). NMH `--poll-credentials` mode; settings.json gates.
- [ ] **R2-P1-05** — Hermes credential source (folds into F-N1).
- [ ] **F-N5** — Mica / Acrylic backdrop on Win11 22621+ via `DwmSetWindowAttribute`.
- [ ] **NX-04** — Edge-snap on drag (within 16 px of monitor edge).
- [ ] **NX-05** — Multi-monitor placement memory.
- [ ] **NX-06** — Catppuccin Latte light theme.
- [ ] **NX-07** — Reduced-motion mode (respect Windows accessibility setting).
- [ ] **NX-08** — Sparkline panel (consume extension's existing `sparklineFor` data).
- [ ] **NX-09** — Tooltip on ring hover.
- [ ] **NX-10** — Embedded log panel.
- [ ] **R2-P2-01** — Working-day Pace integration (Zrnik's `Pace.cs` pattern).

---

## Phase 3 — v0.3+

- [ ] **L-01** — Per-tier alarm sound + message.
- [ ] **L-02** — 7-day "next resets" calendar view.
- [ ] **L-04** — Action Center deep-links on toast buttons.
- [ ] **L-06** — Named pipe between NMH and Widget (drops 250ms FileSystemWatcher latency to <10ms).
- [ ] **L-07** — Plan auto-detection from reset cadence.
- [ ] **L-08** — Burn-rate pace marker on ring (lighter tick).
- [ ] **L-09** — Anomaly / spike detection.
- [ ] **L-10** — Provider plugin contract.
- [ ] **F-N7** — Shell-command webhook on alarm fire.
- [ ] **L-12** — Native messaging companion to keep extension SW alive (mostly handled by F-A4 already).

---

## Under Consideration

- **UC-01** — Avalonia port for Linux + macOS. No demand yet.
- **UC-02** — WinUI 3 / .NET MAUI port. Less predictable than WPF for widget scenarios.
- **L-03** — Win11 Widgets board integration. Tracked, low priority.

---

## Rejected (decisions captured)

- **R-01** — Rainmeter skin path.
- **R-02** — Tauri/Electron port of extension.
- **R-03** — Chromium cookie reads as primary source.
- **R-04** — Re-implementing scraping stack in WPF.
- **R-05** — Pill/oval/rounded backdrops (global rule).
- **R-06** — Paid tier.
- **R-07** — Confetti on reset.
- **R-08** — GPL copyleft license switch.
- **R2-NG-01** — Jira/Toggl integrations (Zrnik direction, dilutive).
- **R2-NG-02** — MSIX packaging (loses per-user install benefits).
- **R2-NG-03** — Port Zrnik's CredentialStore verbatim (read for understanding, write minimal version).
- **R2-NG-04** — Telemetry / opt-in analytics (privacy story).

---

## Themes covered

| Category | Coverage |
|---|---|
| UX | Batch 4, Batch 5, NX-04..NX-09, L-02, L-08 |
| Reliability | F-A4, F-A12, F-A14, R2-P0-02, F-A9, F-A18 |
| Security | R2-P0-01 (CVE), R2-P1-02 (JSON depth), F-A14 (origin), R-Sec-03 (URL scheme) |
| Integrations | F-A1, F-N9, F-A2, F-A4, F-N1, L-10 |
| Accessibility | F-A19, NX-07, UX-Acc-01..04 (Phase 2) |
| Performance | F-A17, L-06 (named pipe) |
| Distribution | F-N10, R2-P1-06, N-17, N-18 |
| Testing | F-A16 |
| Docs | F-N9, N-19, N-20, F-A20, F-A21 |
| Theme | NX-06, F-N5 |
