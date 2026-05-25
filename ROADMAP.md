# Roadmap — single source of truth

**Last updated:** 2026-05-25 · **Current:** v0.1.0 shipped (commit `100165e`); v0.1.1 + v0.2.0 work in progress.

This file is the **executable** TODO. It merges the original three planning files plus Pass 3 post-ship findings. Background and evidence still live in:

- [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md) — Pass 1 audit (positioning, schemas, F-A*/F-N*).
- [RESEARCH_PASS_2.md](RESEARCH_PASS_2.md) — Pass 2 audit (R2-P0-*, R2-P1-*, Pass 1 corrections).
- [RESEARCH_PASS_3.md](RESEARCH_PASS_3.md) — Pass 3 post-ship audit (R3-P0-*, R3-P1-*, R3-P2-*, 5 bugs in shipped code).
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
- [x] [RESEARCH_PASS_3.md](RESEARCH_PASS_3.md) — Pass 3 post-ship audit (5 shipped bugs).

---

## Phase 0' — v0.1.1 bug-fix point release ✅

Surfaced by [RESEARCH_PASS_3.md](RESEARCH_PASS_3.md). Five real bugs in the shipped v0.1.0 code plus three polish items — all landed this session.

- [x] **P0 — R3-P0-01** — R1 ladder walks smallest-first; cascade suppressed by marking missed earlier tiers fired. ([AlarmScheduler.cs](src/QuotaGlass.Widget/Services/AlarmScheduler.cs))
- [x] **P0 — R3-P0-02** — HICON freed via `DestroyIcon` P/Invoke + handle-tracking field on every badge swap and on dispose. ([TrayIconService.cs](src/QuotaGlass.Widget/Services/TrayIconService.cs))
- [x] **P0 — R3-P0-03** — `Brush.Window.MicaBackground` (Mocha.Base@0.35) added to the dictionary; `MicaBackdrop.TryApply` swaps `Brush.Window.Background` to it on success. ([MicaBackdrop.cs](src/QuotaGlass.Widget/Services/MicaBackdrop.cs), [Theme/CatppuccinMocha.xaml](src/QuotaGlass.Widget/Theme/CatppuccinMocha.xaml))
- [x] **P0 — R3-P0-04** — `win-x64;win-arm64` on NMH csproj. ([QuotaGlass.NMH.csproj](src/QuotaGlass.NMH/QuotaGlass.NMH.csproj))
- [x] **P0 — R3-P0-05** — `LadderTierViewModel` + WrapPanel of CheckBoxes wired through `SettingsStore.Update`. ([SettingsPanelViewModel.cs](src/QuotaGlass.Widget/ViewModels/SettingsPanelViewModel.cs), [Views/MainWindow.xaml](src/QuotaGlass.Widget/Views/MainWindow.xaml))
- [x] **P1 — R3-P1-04** — [.github/workflows/ci.yml](.github/workflows/ci.yml) on push + PR; build + test + vulnerable-package audit.
- [x] **P1 — R3-P1-05** — README WAV-only wording corrected; MP3/M4A noted as v0.2.0 NAudio work.
- [x] **P1 — R3-P1-06** — `Settings.Widget.HasShownFirstRunToast` gates `_tray.NotifyFirstRun()` in `MainWindow.Loaded`.
- [x] **bonus** — AlarmScheduler R2 gate loosened (drop-detect by 25-point delta, not absolute < 10).
- [x] **bonus** — `FiredRulesStore.Save` swallows IOException + UnauthorizedAccessException so AV scans can't crash the scheduler.
- [x] **bonus** — Tooltip hints on Warn% / Danger% TextBoxes.

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

### Batch 2 — Schema + integration contract ✅

- [x] **F-N9** — `docs/extension-integration.md` is now the canonical schema spec.
- [x] **F-A1** — `BucketSnapshot.cs` rewritten as `SnapshotMessage` → `ExtensionState` → `ProviderMap` → `ProviderSnapshot` → `Bucket`, mirroring the extension envelope 1:1.
- [x] **F-A5** — `MainViewModel.OnSnapshot` reconciles by `Bucket.Id`; preserves desired display order.
- [x] **F-A12** — `Shared/SchemaVersion.cs` + `IsSupported` check in both `MessagePump` and `SnapshotWatcher`.
- [x] **F-A13** — NMH ack carries version/schema range/server time (already shipped in Batch 1).
- [x] **R2-P1-02** — `MaxDepth = 16` on `SnapshotJsonContext`; `MessagePump` translates depth errors to `"max-depth-exceeded"` ack.
- [x] **F-A14** — `Shared/AllowedOrigins.cs` is the single source of truth for permitted callers; `MessagePump` rejects unlisted origins with `"origin-rejected"`.

### Batch 3 — Toast + TopMost (alarm UX foundation) ✅

- [x] **R2-P0-01** — `Microsoft.Toolkit.Uwp.Notifications` dropped; `dotnet list package --vulnerable` is clean. `Services/ToastService.cs` is hand-rolled on raw `Windows.UI.Notifications`.
- [x] **R2-P0-02** — `Services/TopMostEnforcer.cs` re-asserts `HWND_TOPMOST` on every `EVENT_SYSTEM_FOREGROUND` change via a dedicated STA thread; instantiated in `MainWindow.OnSourceInitialized`.
- [x] **R2-P0-03** — `ToastService.Show` uses `<audio silent="true"/>` and plays the user's WAV via `SoundPlayer.Play()` directly.
- [x] **N-12** — Toast notification adapter shipped (`ToastService`).
- [x] **N-13** — `Services/AlarmScheduler.cs` evaluates the full ladder (24/12/6/3/1h, 30/15/5m, at-reset) every 15 s with fire-once idempotency keyed `<provider>-<bucket>-R1-<lead>-<resetISO>`; persisted in `Services/FiredRulesStore.cs` at `%LOCALAPPDATA%\QuotaGlass\fired-rules.json`.
- [x] **N-14** — Zero-state R3 + R2 renewal-arrived + U1 75/90/95 threshold rules all live in `AlarmScheduler.EvaluateProvider`.

### Batch 4 — Widget polish ✅

- [x] **F-N8** — `App.OnStartup` parses `--inject-fake-snapshot`; `Services/FakeSnapshotInjector` writes a deterministic 4-bucket snapshot.
- [x] **F-N6** — Card `MouseLeftButtonUp` → `Process.Start(analyticsUrl) { UseShellExecute = true }` via `BucketViewModel.AnalyticsUrl`.
- [x] **F-A9** — `MainViewModel.UpdateStaleness` colors `StatusKind` + dims each ring via `BucketViewModel.StaleOpacity` at 10 min / 30 min thresholds.
- [x] **F-A19** — `Brush.Card.MutedText` bumped from Overlay1 to Overlay2 for ≥4.5:1 contrast on Mantle@0.88.
- [x] **R2-P1-03** — `Services/PaceCalculator` linear-extrapolates between consecutive snapshots; shown only when pace would exhaust before reset.
- [x] **F-A14** — (already shipped in Batch 2 — listed here for completeness.)

### Batch 5 — Tray + first-run ✅

- [x] **F-N4** — `Services/TrayIconService.cs` uses WinForms `NotifyIcon` (no extra packages — enabled via `<UseWindowsForms>true</UseWindowsForms>` alongside WPF). Right-click menu: Show / Hide / Refresh / Settings… / Quit. Double-click toggles widget. Generates its own runtime tray icon with worst-bucket badge color (green<60<peach<85<red). First-run balloon tip.
- [x] **F-N3** — `Services/HealthCheck.cs` probes registry + snapshot.json; `ViewModels/SetupCardViewModel.cs` polls every 2s; XAML setup card shows 3 steps with Install / Run --register / Help buttons. Card auto-collapses when all green.
- [x] **F-N10** — ARM64 added to `RuntimeIdentifiers` (advance of Batch 8).

---

## Phase 1 — v0.1.0 ship

### Batch 6 — Settings ✅

- [x] **N-15** — Embedded settings panel inside MainWindow; expand/collapse button at bottom; CheckBoxes for alarms-enabled + autostart; TextBoxes for warn/danger %; current ladder + custom-sound path.
- [x] **N-16** — `Services/SettingsStore.cs` persists to `%LOCALAPPDATA%\QuotaGlass\settings.json` via atomic write; source-generated JSON; `Changed` event re-applies to `AlarmScheduler` so threshold/ladder/sound edits take effect immediately. `Services/AutostartRegistration.cs` writes the HKCU\…\Run entry when Autostart toggled.

### Batch 7 — Cross-repo bridge ⏸ (drop-in ready)

Existing in-progress work in `~/repos/AI-Usage_Tracker` (~20 files staged on 2026-05-25) touches the same files as F-A2/F-A4. To avoid clobbering that work, the bridge implementation is documented + sample-coded at [`docs/bridge-integration.md`](docs/bridge-integration.md) for drop-in after the upstream branch merges.

- [x] **F-A2** — Manifest `"key"` + Chrome-ID derivation steps documented; `QuotaGlass.NMH/AllowedOrigins.cs` already has the single source of truth (`AiUsageTrackerChromeId`) to replace once the ID is computed.
- [x] **F-A4** — Full `bridge.js` implementation written (persistent port + 25 s keepalive ping + lazy reconnect + safe disconnect handling); manifest permission patch + `background.js` hook documented. Awaiting drop-in.

### Batch 8 — Distribution ✅

- [x] **F-N10** — `win-x64;win-arm64` on both csprojs.
- [x] **R2-P1-08** — `ToastService.AppUserModelId = "com.sysadmindoc.QuotaGlass.Widget"`; Inno script sets the same AUMID on the Start Menu shortcut (`[Icons]` `AppUserModelID:`).
- [x] **R2-P1-06** — `Services/UpdateChecker.cs` queries `api.github.com/repos/SysAdminDoc/QuotaGlass/releases/latest`, finds the matching arch asset, downloads to `%TEMP%`, writes a PS1 self-replace script. Lazy — only runs on demand.
- [x] **N-17** — `installer/quotaglass.iss` per-user install to `%LOCALAPPDATA%\Programs\QuotaGlass\`, registers NMH, drops AUMID-bearing Start Menu shortcut, optional autostart task, runs `--unregister` on uninstall, multi-arch via `/DAppArch=x64|arm64`.
- [x] **N-18** — `.github/workflows/release.yml` `workflow_dispatch` w/ `version` input, matrix on `[x64, arm64]`, publishes single-file framework-dependent EXEs, builds Inno installer, uploads to GH Release.

### Batch 9 — Logging + observability ✅

- [x] **F-A10** — `Logger.RotateIfNeeded` rolls when current file exceeds 10 MB; `PruneOldFiles` deletes files older than 14 days on `Init`.
- [x] **R-Rec-02** — `QuotaGlass.NMH.exe --purge` wipes `%LOCALAPPDATA%\QuotaGlass\` (folder retained, contents deleted).
- [x] **R-Log-03** — `Services/WidgetLogger.cs` mirrors NMH logger (10 MB cap, 14-day retention, daily file); wired from `App.OnStartup`; captures `DispatcherUnhandledException`.
- [ ] **R-Log-02** — Correlation IDs — deferred to v0.2. Current logging keys to caller-origin + bucket counts; correlation ID adds value only once we have multi-extension fan-in.

### Batch 10 — Tests + final docs ✅ (partial)

- [x] **F-A16** — `test/QuotaGlass.Tests/` xUnit project; 11 passing tests covering AtomicJsonFile round-trip + missing/invalid file paths, schema version range, full extension-payload fidelity deserialization, MaxDepth depth-bomb rejection, unknown-field tolerance. Solution `dotnet test` green.
- [x] **N-19** — README updated with ARM64 instructions, `--purge` flag, `--inject-fake-snapshot` dev mode, `dotnet test` instructions.
- [ ] **N-20** — Screenshots — need an actual runtime to capture; deferred until user manually runs the widget.

---

## Phase 2 — v0.2.0 polish + true differentiator

Pass 3 additions ✅ — all shipped in v0.2.0 (2026-05-25).

- [x] **P1 — R3-P1-01** — Single-instance Mutex. ([App.xaml.cs](src/QuotaGlass.Widget/App.xaml.cs))
- [x] **P1 — R3-P1-02** — `--collect-diagnostics` flag. ([NMH/Diagnostics.cs](src/QuotaGlass.NMH/Diagnostics.cs))
- [x] **P1 — R3-P1-03** — U2 pace alarm tier. ([AlarmScheduler.cs](src/QuotaGlass.Widget/Services/AlarmScheduler.cs))
- [x] **P1 — R3-P1-08** — Tray "Check for updates" menu entry. ([MainWindow.xaml.cs](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs))
- [x] **P2 — R3-P1-07** — Setup card "Dismiss 24h". ([SetupCardViewModel.cs](src/QuotaGlass.Widget/ViewModels/SetupCardViewModel.cs))
- [x] **P2 — R3-P2-02** — `HistoryStore` ring buffer feeding NX-08. ([HistoryStore.cs](src/QuotaGlass.Widget/Services/HistoryStore.cs))
- [x] **P2 — R3-P2-05** — DPI-safe Viewbox-wrap of ring center text.
- [x] **P2 — R3-P2-06** — Per-bucket mute/snooze right-click menu.
- [x] **P2 — R3-P2-07** — Tray "Reset widget position" entry.
- [x] **P2 — NX-04** — Edge-snap on drag (16 px threshold).
- [x] **P2 — NX-06** — Catppuccin Latte light theme + runtime swap.
- [x] **P2 — NX-08** — Sparkline panel rendered via new `Controls/Sparkline.cs`.
- [x] **P2 — NX-09** — Multiline ring hover tooltip.
- [x] **P2 — NX-10** — Embedded log panel inside the settings panel.

- [ ] **F-N1** — Direct credential reading (`%USERPROFILE%\.claude\.credentials.json`, `.codex\auth.json`, `.hermes\auth.json`). NMH `--poll-credentials` mode; settings.json gates. **(Large feature; warrants its own session.)**
- [ ] **R2-P1-05** — Hermes credential source (folds into F-N1).
- [x] **F-N5** — `Services/MicaBackdrop.cs` applies Mica system backdrop on Win11 22621+ via `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` + dark immersive title bar. No-op on Win10.
- [ ] **NX-04** — Edge-snap on drag (within 16 px of monitor edge).
- [x] **NX-05** — `MainWindow.OnSourceInitialized` restores `Widget.X`/`Widget.Y` from settings if still on-screen; `LocationChanged` persists. Multi-monitor aware via `Screen.AllScreens`.
- [ ] **NX-06** — Catppuccin Latte light theme.
- [x] **NX-07** — `MainViewModel.ReducedMotion` reads `SystemParameters.ClientAreaAnimation`; bound to `RadialRing.ReducedMotion` so v0.2 animation transitions can honor it.
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
