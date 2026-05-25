# Changelog

All notable changes to QuotaGlass will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Nothing yet — v0.4.0 just shipped.

## [0.4.0] — 2026-05-25

Insights + audio release. Closes the remaining Phase-3 backlog items
that didn't depend on toast actions or named-pipe transport.

### Added

- **L-02** — Collapsible "Next 7 days" reset calendar inside the settings panel. Groups every bucket's `ResetIso` into per-local-day groups with "Today / Tomorrow / weekday" headers. ([ViewModels/CalendarViewModel.cs](src/QuotaGlass.Widget/ViewModels/CalendarViewModel.cs))
- **L-07** — Plan auto-detection from reset cadence + bucket-kind expansion. Fills `ProviderSnapshot.Plan` when the extension didn't set it. Heuristics: Claude (`max-20x` / `max-5x` / `pro` / `free`), Codex (`plus` / `free`). ([Shared/PlanInference.cs](src/QuotaGlass.Shared/PlanInference.cs))
- **L-09** — Anomaly / spike detection. A new `AnomalyDetector` in Shared flags samples that jump ≥3× the median delta of recent positive deltas (and ≥5 percentage points absolute). Wired as a new `U3` rule family inside `AlarmScheduler`; fires once per resetISO per bucket. ([Shared/AnomalyDetector.cs](src/QuotaGlass.Shared/AnomalyDetector.cs))
- **MP3 / M4A / AAC / WMA support** for custom alarm sounds. WAV still routes through `SoundPlayer` for lowest latency; everything else routes through WPF `MediaPlayer` (Media Foundation). OpenFileDialog filter widened. No NAudio dependency. ([Services/ToastService.cs](src/QuotaGlass.Widget/Services/ToastService.cs))
- **R3-P2-01 scaffold** — Multi-account identifier surfaced in bucket-card hover tooltip. `ProviderSnapshot.OrgId` / `AccountId` is rendered as the last-8-char tail ("…2c4f99a1") so multi-account users can disambiguate cards. Full side-by-side columns still wait for F-N1 to land on real multi-account data. ([ViewModels/BucketViewModel.cs](src/QuotaGlass.Widget/ViewModels/BucketViewModel.cs))

### Added — tests

- **7 new `PlanInferenceTests`** — covers each Claude / Codex heuristic branch plus the "already-set plan stays" and "empty bucket list returns null" edges.
- **5 new `AnomalyDetectorTests`** — exercises sub-window size, steady growth, sudden burst, sub-threshold jump, reset-drop semantics.
- `HistorySample` moved from Widget to Shared so it's reachable from unit tests without pulling WPF in.

### Changed

- README MP3/M4A wording reverted to "supported" (was "WAV-only, planned" in v0.1.1).

### Known limitations carried forward

- L-04 toast-actions (Snooze / Open buttons) — Toolkit-vs-COM-activator decision still deferred.
- L-06 named-pipe NMH↔Widget transport — defer; 250 ms debounced FileSystemWatcher is acceptable.
- L-10 provider plugin contract — needs a real second-provider use case to anchor the design.
- L-12 NM-companion to keep extension SW alive — already mostly handled by `bridge.js` 25 s ping.
- Manual screenshots for `assets/screenshots/` — still open.

## [0.3.0] — 2026-05-25

Power-user release: closes the bulk of the Pass 3 v0.3 queue plus the
biggest Phase-3 items (F-N1 credential reading, F-N7 webhooks, L-01
per-tier sounds, L-08 pace marker). Adds an accessibility batch plus
the first SECURITY.md / CONTRIBUTING.md / .gitattributes.

### Added — biggest

- **F-N1** — Direct OAuth credential reading. `QuotaGlass.NMH.exe --poll-credentials [--interval-minutes N]` is a long-running mode that:
  - probes `%USERPROFILE%\.claude\.credentials.json`, `%USERPROFILE%\.codex\auth.json`, and `%USERPROFILE%\.hermes\auth.json`;
  - calls the matching provider with the access token (minimal `/v1/messages` ping for Claude; WHAM usage GET for Codex);
  - parses `anthropic-ratelimit-unified-{5h,7d}-*` headers and ChatGPT WHAM `primary_window` / `secondary_window` JSON;
  - writes the resulting `SnapshotMessage` through the same `AtomicJsonFile` sink the extension bridge uses (source: `local-creds`).
  - Closes the "browser must be open" gap. Six credential schema shapes handled. ([NMH/CredentialPoller.cs](src/QuotaGlass.NMH/CredentialPoller.cs))
- **F-N7** — Shell-command webhook on alarm fire. Settings panel exposes a `cmd /c …` command run with five env vars (`QG_PROVIDER`, `QG_BUCKET_ID`, `QG_PERCENT`, `QG_RESET_ISO`, `QG_TIER`). 5 s self-kill. Power users wire ntfy / Discord / Home Assistant without QuotaGlass shipping the integrations. ([AlarmScheduler.cs](src/QuotaGlass.Widget/Services/AlarmScheduler.cs))
- **R3-P2-04** — Focus Assist / DND awareness via `SHQueryUserNotificationState`. Suppressed tiers are still marked fired so they don't replay later (matches the R1 cold-start semantics). Toggleable in settings. ([Services/FocusAssist.cs](src/QuotaGlass.Widget/Services/FocusAssist.cs))
- **L-08** — Burn-rate pace marker on the ring. A lighter tick at the projected exhaustion angle when `PaceCalculator` says burn will exhaust before reset. Driven by the new `BucketViewModel.PaceMarkerPercent`. ([Controls/RadialRing.cs](src/QuotaGlass.Widget/Controls/RadialRing.cs))

### Added — UI / config

- **L-01** — Per-tier alarm sound UI. The `Reset (R2) sound` and `Zero-state (R3) sound` slots that existed in `Settings.Alarms` since v0.1.0 are now pickable from the settings panel. The `Custom (R1/U1/U2) sound` row remains as before.
- **Pace toast toggle** — Settings exposes `Alarms.PaceEnabled` (default ON) so users can turn off the new U2 pace-warning tier without touching JSON.
- **UX-Acc batch** — Bucket cards are now keyboard-focusable (`Focusable=True`, `IsTabStop=True`); Enter/Space opens the analytics URL; Shift+F10 / Apps opens the snooze context menu. AutomationProperties for the card include the full hover tooltip so screen readers narrate provider + label + percent + reset time.

### Added — refactor + tests

- **LadderEvaluator** — Pure decision logic for the R1 ladder extracted into `src/QuotaGlass.Shared/LadderEvaluator.cs` so it's unit-testable without WPF deps. AlarmScheduler now delegates to it; behavior is identical. ([Shared/LadderEvaluator.cs](src/QuotaGlass.Shared/LadderEvaluator.cs))
- **6 new tests in `LadderEvaluatorTests`** — locks in R3-P0-01: cold-start fires smallest tier, walking tick-by-tick fires each tier once, past-grace returns no decision, already-fired-5m falls through to at-reset after the window, before-any-tier-elapses returns null, custom grace works. ([test/QuotaGlass.Tests/LadderEvaluatorTests.cs](test/QuotaGlass.Tests/LadderEvaluatorTests.cs))
- **10 new tests in `CredentialPollerTests`** — pure-functional pieces of F-N1: access-token extraction across 8 schema shapes; ratio → percent normalization; epoch (seconds/ms) and ISO date parsing; Claude unified-5h/7d header → ProviderSnapshot; Codex WHAM `primary_window`/`secondary_window` JSON. Test csproj now references QuotaGlass.NMH too. ([test/QuotaGlass.Tests/CredentialPollerTests.cs](test/QuotaGlass.Tests/CredentialPollerTests.cs))

### Added — repo hygiene

- **[.gitattributes](.gitattributes)** — normalize line endings (LF in index, platform-native on checkout); CRLF forced on `.bat` / `.cmd` / `.ps1` / `.iss`; binary markers for media + executables.
- **[SECURITY.md](SECURITY.md)** — coordinated-disclosure policy via GitHub Security Advisories; explicit list of acceptable PoC paths; commit-to lines.
- **[CONTRIBUTING.md](CONTRIBUTING.md)** — match-existing-style guide; no Co-Authored-By trailer; no surprise NuGet packages; layout + style + PR cheat-sheet.

### Known limitations carried forward

- F-N1 credential probe still needs live validation against a 2026-Q2 Claude Code / Codex CLI install on a desktop with the SDK. Schema may drift; the parser is tolerant but not omniscient.
- MP3 / M4A toast audio via NAudio — deferred to v0.4.
- R3-P2-01 multi-account columns within a provider — needs F-N1 to land on real data first; deferred to v0.4.
- Manual screenshots for `assets/screenshots/` — still open.
- Toast actions (Snooze / Open) — Toolkit-or-COM-activator decision deferred to v0.4.

## [0.2.0] — 2026-05-25

Polish + first-true-differentiator release. Closes a large chunk of Pass 3's v0.2.0 queue and most of the original NX-* polish backlog.

### Added

- **R3-P1-01** — Single-instance Mutex (`Global\QuotaGlass.Widget.Instance.*`). A second launch focuses the first window and exits, eliminating the `settings.json` lost-update race. ([App.xaml.cs](src/QuotaGlass.Widget/App.xaml.cs))
- **R3-P1-02** — `QuotaGlass.NMH.exe --collect-diagnostics` zips logs + redacted snapshot (orgId/accountId scrubbed) + redacted settings (custom WAV paths truncated to last 12 chars) + a meta.txt (OS, NMH version, registry presence) into `%TEMP%\quotaglass-diag-{ts}.zip`. ([NMH/Diagnostics.cs](src/QuotaGlass.NMH/Diagnostics.cs))
- **R3-P1-03** — U2 pace alarm tier. PaceCalculator output now also feeds AlarmScheduler so a "Claude pace warning" toast fires once per reset window when the burn-rate forecast says the bucket will exhaust before reset. Toggleable via `AlarmScheduler.PaceEnabled`. ([AlarmScheduler.cs](src/QuotaGlass.Widget/Services/AlarmScheduler.cs))
- **R3-P1-08** — Tray menu "Check for updates…" wires `UpdateChecker.CheckAsync` → confirm dialog → `LaunchSelfReplace`. ([TrayIconService.cs](src/QuotaGlass.Widget/Services/TrayIconService.cs), [MainWindow.xaml.cs](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs))
- **R3-P1-07** — "Dismiss 24h" button on the Setup card. Persisted as `Widget.SetupCardDismissedUntilUtc` in `settings.json`. ([SetupCardViewModel.cs](src/QuotaGlass.Widget/ViewModels/SetupCardViewModel.cs))
- **R3-P2-07** — Tray menu "Reset widget position" — resets `Widget.X/Y` to `(40, 40)`. Recovery affordance for unplugged-monitor scenarios. ([MainWindow.xaml.cs](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs))
- **NX-04** — Edge-snap on drag — within 16 px of the current monitor's working-area edge, the widget snaps. Multi-monitor aware. ([MainWindow.xaml.cs](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs))
- **R3-P2-05** — DPI-safe ring center text: the countdown is Viewbox-wrapped so it scales to fit at every DPI (no more overflow at ≥200% scaling). ([MainWindow.xaml](src/QuotaGlass.Widget/Views/MainWindow.xaml))
- **NX-09** — Ring-hover tooltip: multiline panel showing provider, label, percent, reset time, pace forecast, and click/right-click hints. ([MainWindow.xaml](src/QuotaGlass.Widget/Views/MainWindow.xaml), [BucketViewModel.cs](src/QuotaGlass.Widget/ViewModels/BucketViewModel.cs))
- **R3-P2-06** — Per-bucket mute/snooze via right-click context menu on each card (1h / 6h / 24h / until-reset / unsnooze). Persisted in `Alarms.SnoozedBucketsUntilUtc`. ([MainWindow.xaml.cs](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs), [AlarmScheduler.cs](src/QuotaGlass.Widget/Services/AlarmScheduler.cs))
- **NX-06** — Catppuccin Latte light theme. Settings panel exposes Mocha / Latte radio buttons; theme persists. ([Theme/CatppuccinLatte.xaml](src/QuotaGlass.Widget/Theme/CatppuccinLatte.xaml), [Services/ThemeService.cs](src/QuotaGlass.Widget/Services/ThemeService.cs))
- **R3-P2-02 + NX-08** — Durable per-bucket history ring buffer at `%LOCALAPPDATA%\QuotaGlass\history.json` (24 samples × N buckets, atomic write, IOException-tolerant). Rendered as a 24-sample sparkline under each bucket card via a new `Controls/Sparkline.cs` (DependencyProperty-based, ~100 LOC, no third-party charting). ([Services/HistoryStore.cs](src/QuotaGlass.Widget/Services/HistoryStore.cs), [Controls/Sparkline.cs](src/QuotaGlass.Widget/Controls/Sparkline.cs))
- **NX-10** — Embedded log panel — collapsed-by-default toggle inside the settings panel shows the last 24 lines of today's NMH + Widget logs; auto-refreshes every 3 seconds while open. ([ViewModels/LogPanelViewModel.cs](src/QuotaGlass.Widget/ViewModels/LogPanelViewModel.cs))

### Known limitations carried forward

- F-N1 direct credential reading (Claude Code CLI `.credentials.json` + Codex `auth.json` + Hermes `auth.json`) — large feature; deferred to v0.3.
- R3-P2-04 Focus Assist awareness — deferred to v0.3.
- R3-P2-01 multi-account columns within a provider — needs F-N1; deferred to v0.3.
- MP3 / M4A toast audio (NAudio integration) — deferred to v0.3.
- Manual screenshots for `assets/screenshots/` — still empty; needs a runtime capture pass.

## [0.1.1] — 2026-05-25

Bug-fix point release surfaced by [RESEARCH_PASS_3.md](RESEARCH_PASS_3.md) post-ship audit of v0.1.0.

### Fixed

- **R3-P0-01** — R1 alarm ladder no longer fires a stale-tier cascade on cold start. The scheduler now walks smallest-lead-first and fires the closest-to-now tier; every earlier missed tier is marked fired-but-suppressed so a widget launched 5 min before reset no longer plays "Claude resets in 24 hours" then 6 more wrong toasts at 15 s intervals. ([Services/AlarmScheduler.cs](src/QuotaGlass.Widget/Services/AlarmScheduler.cs))
- **R3-P0-02** — Tray icon no longer leaks Win32 HICON handles. `Icon.FromHandle` does not own its handle, and `Icon.Dispose` does not call `DestroyIcon`; `TrayIconService` now tracks the active HICON and releases it via P/Invoke on every badge swap and on dispose. ([Services/TrayIconService.cs](src/QuotaGlass.Widget/Services/TrayIconService.cs))
- **R3-P0-03** — Mica system backdrop is now visible on Win11 22621+. The opaque `Brush.Window.Background` (`Mocha.Base @ 0.92`) was occluding DWM Mica; `MicaBackdrop.TryApply` now swaps the resource to `Brush.Window.MicaBackground` (`Mocha.Base @ 0.35`) on success so the chrome border becomes translucent to the composition layer. ([Services/MicaBackdrop.cs](src/QuotaGlass.Widget/Services/MicaBackdrop.cs), [Theme/CatppuccinMocha.xaml](src/QuotaGlass.Widget/Theme/CatppuccinMocha.xaml))
- **R3-P0-04** — `QuotaGlass.NMH.csproj` `<RuntimeIdentifiers>` now includes `win-arm64` (was `win-x64` only despite the release matrix building both archs). ([QuotaGlass.NMH.csproj](src/QuotaGlass.NMH/QuotaGlass.NMH.csproj))
- **R3-P0-05** — Settings panel now exposes per-tier checkbox toggles for the alarm ladder (24h / 12h / 6h / 3h / 1h / 30m / 15m / 5m / at-reset). Unchecking a tier suppresses its toast for the current reset window. Brief promised this since day 1; was display-only in v0.1.0. ([ViewModels/SettingsPanelViewModel.cs](src/QuotaGlass.Widget/ViewModels/SettingsPanelViewModel.cs), [Views/MainWindow.xaml](src/QuotaGlass.Widget/Views/MainWindow.xaml))
- **AlarmScheduler R2 gate** — renewal-arrived rule no longer drops legitimate renewals when the user burns >10% in the first minute post-reset. The gate now requires a *real drop* (`prevPercent > 25 && bucket.PercentUsed < prevPercent - 25`) instead of `< 10` absolute. ([Services/AlarmScheduler.cs](src/QuotaGlass.Widget/Services/AlarmScheduler.cs))
- **FiredRulesStore** — `Save()` no longer crashes the scheduler when an AV scan or share lock collides with the JSON write. ([Services/FiredRulesStore.cs](src/QuotaGlass.Widget/Services/FiredRulesStore.cs))
- **R3-P1-06** — "QuotaGlass is in your tray" balloon tip now fires only on the very first run, not every launch. ([Services/SettingsStore.cs](src/QuotaGlass.Widget/Services/SettingsStore.cs), [Views/MainWindow.xaml.cs](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs))

### Changed

- **R3-P1-05** — README + this CHANGELOG no longer claim MP3/M4A support. Shipped behavior is WAV-only via `System.Media.SoundPlayer`; MP3/M4A (via NAudio 2.x) is planned for v0.2.0. ([README.md](README.md))
- Settings panel Warn % / Danger % fields now carry tooltip hints ("Ring turns peach above this percentage" / "Ring turns red above this percentage").

### Added

- **R3-P1-04** — `.github/workflows/ci.yml` runs `dotnet build` + `dotnet test` + vulnerable-package audit on every push and PR (release.yml only ran on manual dispatch). ([.github/workflows/ci.yml](.github/workflows/ci.yml))

### Known limitations carried forward
- Pace-burn-rate forecast is computed and shown in the card footer, but does not yet fire a notification tier — slated as R3-P1-03 for v0.2.0.
- Screenshots in `assets/screenshots/` still empty (needs a manual capture pass).

## [0.1.0] — 2026-05-25 (pending tag)

## [0.1.0] — 2026-05-25 (pending tag)

### Added
- **Catppuccin Mocha glass widget** — borderless always-on-top WPF window with draggable chrome and per-bucket radial-ring countdowns.
- **Native messaging host** with 4-byte LE length-prefix framing, schema-versioned snapshot envelope (matches AI-Usage_Tracker `state` shape 1:1), persistent ping/pong keepalive, origin allow-list enforcement, JSON depth-bomb rejection (`MaxDepth=16`), and forward-compat ack handshake.
- **Alarm scheduler** evaluating four rule families: R1 imminent-reset ladder (24h/12h/6h/3h/1h/30m/15m/5m/at-reset), R2 renewal-arrived, R3 zero-state, U1 thresholds (75/90/95). Fire-once idempotency keys persisted at `%LOCALAPPDATA%\QuotaGlass\fired-rules.json` with 14-day retention.
- **Custom-sound toasts** via raw `Windows.UI.Notifications` (no `Microsoft.Toolkit.Uwp.Notifications` dependency); custom audio plays via `System.Media.SoundPlayer.Play()` alongside `<audio silent="true"/>` because the legacy toast XML schema silently ignores file:/// paths.
- **`TopMostEnforcer`** WinEvent foreground hook re-asserts HWND_TOPMOST so UAC dialogs and fullscreen apps can't demote the widget.
- **System tray** with right-click menu (Show / Hide / Refresh / Settings / Quit), double-click toggle, and runtime-rendered worst-bucket badge icon.
- **First-run Setup Checklist** card that probes for extension install + NMH registration + first snapshot, auto-hides when green.
- **Embedded settings panel** with refresh interval, alarm enable/ladder/thresholds, custom-sound picker, autostart toggle (HKCU\…\Run), display thresholds; persisted via atomic JSON write to `%LOCALAPPDATA%\QuotaGlass\settings.json`.
- **Pace footer** — 2-sample linear extrapolation; shown only when projected exhaustion is before the next reset.
- **Stale-snapshot visual state** — rings dim at 10 min, dim further + "STALE" prefix at 30 min.
- **Click bucket card → open analytics page** in default browser via `Process.Start` (URL scheme restricted to http/https).
- **Self-hosted updater** (`Services/UpdateChecker.cs`) — queries GitHub Releases API, downloads matching arch asset, runs PowerShell self-replace script.
- **Inno Setup installer** (`installer/quotaglass.iss`) — per-user install, AUMID-bearing Start Menu shortcut, optional autostart, runs `--register` on install + `--unregister` on uninstall, multi-arch (`x64`/`arm64`).
- **GitHub release workflow** (`.github/workflows/release.yml`) — `workflow_dispatch`, matrix on `[x64, arm64]`, single-file framework-dependent EXEs + Inno installer, auto-upload to GH Release.
- **Log rotation** — both NMH and widget loggers cap files at 10 MB and prune older than 14 days.
- **`--purge` NMH flag** wipes `%LOCALAPPDATA%\QuotaGlass\` for clean re-install.
- **`--inject-fake-snapshot` widget flag** writes deterministic snapshot for solo widget dev.
- **WCAG AA contrast fix** — `Brush.Card.MutedText` bumped from Overlay1 to Overlay2 for ≥4.5:1 on Mantle@0.88.
- **`test/QuotaGlass.Tests/` xUnit project** — 11 passing tests across atomic-write round-trip, schema versioning, full extension-payload fidelity, JSON depth-bomb rejection, unknown-field tolerance.
- **`docs/extension-integration.md`** canonical schema spec.
- **`docs/bridge-integration.md`** drop-in for the AI-Usage_Tracker side (keypair generation, `"key"` field, `"nativeMessaging"` permission, `bridge.js` with persistent port + 25 s ping + reconnect, `background.js` hook).

### Security
- Eliminated transitive `System.Drawing.Common 4.7.0` Critical CVE (GHSA-rxg9-xrhp-64gj) by dropping `Microsoft.Toolkit.Uwp.Notifications` package. `dotnet list package --vulnerable` is clean.

### Known limitations
- Screenshots in `assets/screenshots/` not yet captured (needs runtime).
- Extension-side bridge merge gated on AI-Usage_Tracker's in-progress branch landing; drop-in code complete in `docs/bridge-integration.md`.
- Toast actions (Snooze / Open buttons) deferred to v0.2.
