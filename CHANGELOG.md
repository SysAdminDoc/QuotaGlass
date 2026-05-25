# Changelog

All notable changes to QuotaGlass will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Nothing yet — v0.8.0 just shipped.

## [0.8.0] — 2026-05-25

UX refactor release. Settings panel reorganized into 4 expandable sub-sections (Alarms / Display / Integration / Advanced). Two of the largest inline XAML blocks (calendar + log) extracted into dedicated UserControls.

### Refactored

- **Settings panel sub-sections** — every existing control is unchanged structurally; just wrapped into four `Expander` headers so users with 14+ knobs don't see them all at once. Alarms is open by default (most-used); Display / Integration / Advanced collapse. ([Views/MainWindow.xaml](src/QuotaGlass.Widget/Views/MainWindow.xaml))
- **CalendarPanelView** extracted from MainWindow.xaml into its own [Views/CalendarPanelView.xaml](src/QuotaGlass.Widget/Views/CalendarPanelView.xaml) + .xaml.cs. DataContext is `MainViewModel.Calendar`; the toggle button delegates to `CalendarViewModel.Toggle()`.
- **LogPanelView** extracted from MainWindow.xaml into [Views/LogPanelView.xaml](src/QuotaGlass.Widget/Views/LogPanelView.xaml). DataContext is `MainViewModel.LogPanel`.
- MainWindow.xaml shrinks from 447 → ~390 lines as a result; the two extracted controls add 44 + 34 lines of self-contained XAML.

### Added

- **`.editorconfig`** — consistent line endings, indent width, and C# naming-style rules across contributors. Matches the conventions already in use (LF in source, CRLF in csproj/sln/xaml, 4-space C#, `_camelCase` private fields, `IPascal` interfaces).

### Carried into v0.9+

- `SnapshotWatcher.Merge` unit tests (needs the test project to reach into the Widget assembly — requires a TFM bump for the tests; defer until we have a real CI-validated build).
- L-10 provider plugin contract.
- N-20 manual screenshots.
- RESX satellite assemblies (Strings.cs scaffold is ready, but actual locale files wait until v0.7 stabilizes in user hands).

## [0.7.0] — 2026-05-25

Multi-account + accessibility + low-latency transport release. Closes the remaining bigger Pass 4 carry-forward items (R4-N5 / R4-N8 / R4-N6 / R4-N9) plus the v0.6 deferred multi-account columns.

### Added

- **R4-N5 / R3-P2-01 full** — Multi-account columns. Schema bumped to v3; `ProviderMap` gains optional `ClaudeAccounts` + `CodexAccounts` arrays. `MainViewModel.OnSnapshot` expands each provider's primary + secondary accounts into per-account bucket sets, all rendered via the existing card flow (`BucketViewModel.AccountLabel` from v0.4 already disambiguates them in the hover tooltip). Older receivers still see the primary account through the unchanged `Claude` / `Codex` fields. ([Shared/BucketSnapshot.cs](src/QuotaGlass.Shared/BucketSnapshot.cs), [Shared/SchemaVersion.cs](src/QuotaGlass.Shared/SchemaVersion.cs), [ViewModels/MainViewModel.cs](src/QuotaGlass.Widget/ViewModels/MainViewModel.cs), [docs/extension-integration.md](docs/extension-integration.md))
- **R4-N8** — Windows High Contrast theme + "Follow system" mode. New [Theme/HighContrast.xaml](src/QuotaGlass.Widget/Theme/HighContrast.xaml) binds every brush to `DynamicResource SystemColors.*Key` so the active HC scheme drives every pixel. `ThemeService.ResolveTheme("system")` queries `SystemParameters.HighContrast` and the `HKCU\…\Personalize\AppsUseLightTheme` registry value to pick Mocha / Latte / HighContrast at launch. Settings panel exposes four radio buttons (Mocha / Latte / High contrast / Follow system). ([Services/ThemeService.cs](src/QuotaGlass.Widget/Services/ThemeService.cs))
- **L-06 / R4-N6** — Named-pipe NMH↔Widget transport. NMH now publishes every persisted snapshot to `\\.\pipe\QuotaGlass.Snapshot` in 4-byte-LE-prefixed JSON; widget's `SnapshotPipeClient` consumes them on a background thread and marshals to the dispatcher. Cuts snapshot→render latency from the 250 ms FileSystemWatcher floor to <10 ms. Pipe failures gracefully fall back to the watcher path. ([Shared/SnapshotPipe.cs](src/QuotaGlass.Shared/SnapshotPipe.cs), [NMH/SnapshotPipeServer.cs](src/QuotaGlass.NMH/SnapshotPipeServer.cs), [Services/SnapshotPipeClient.cs](src/QuotaGlass.Widget/Services/SnapshotPipeClient.cs))
- **R4-N9** — Localization scaffold. New [Resources/Strings.cs](src/QuotaGlass.Widget/Resources/Strings.cs) hosts every English UI literal keyed for future RESX swapping. `Strings.SetUiCulture` exposes the entry point future versions wire to a settings field. No user-visible change in v0.7 — strings still resolve from the default dictionary — but consumers can migrate one widget at a time.

### Changed

- Schema v3 ([Shared/SchemaVersion.cs](src/QuotaGlass.Shared/SchemaVersion.cs)) — `Current` = `Max` = 3, `Min` stays 1. All additions are additive so v1/v2 senders work unchanged.
- `ThemeService.Apply` recognizes the `HighContrast` dictionary in its swap loop.

### Carried into v0.8+

- Architecture refactor (MainWindow.xaml UserControl extraction + settings sub-sections) — substantial XAML refactor; defer until CI has been green across several releases.
- L-10 provider plugin contract — still no real second-provider use case.
- N-20 manual screenshots — still needs a runtime.

## [0.6.0] — 2026-05-25

Toast actions + schema v2. Closes [RESEARCH_PASS_4.md](RESEARCH_PASS_4.md)'s v0.6 batch (R4-N2 / R4-N3 / R4-N7) plus the carry-forward test coverage gaps. Snooze and Open Analytics buttons now appear inside every bucket toast; sparklines render on fresh installs because the extension can bundle 24-sample history in the snapshot envelope.

### Added

- **L-04 / R4-N2** — Toast actions ("Snooze 1h" and "Open analytics") via hand-rolled COM activator. New [Services/ToastActivator.cs](src/QuotaGlass.Widget/Services/ToastActivator.cs) implements `INotificationActivationCallback` so Action Center routes button clicks back to the running widget process. Installer registers the activator CLSID (`{4F1B3F6E-2D8C-4E83-9C12-9B0B17F8D2A2}`) at `HKCU\Software\Classes\CLSID\…` and binds it to the Start Menu shortcut via `AppUserModelToastActivatorCLSID`. `App.OnStartup` also calls `CoRegisterClassObject` at runtime so the live process receives activations. **No `Microsoft.Toolkit.Uwp.Notifications` re-add** — preserves the v0.1.1 CVE win. ([Services/ToastActivatorRegistration.cs](src/QuotaGlass.Widget/Services/ToastActivatorRegistration.cs), [installer/quotaglass.iss](installer/quotaglass.iss))
- **R4-N3** — Wire schema bumped to v2 (`SchemaVersion.Max = 2`). Adds optional `state.history: { bucketId: [{ts, percentUsed}, …] }`. Widget merges incoming samples into `HistoryStore` via the new `MergeIncoming` method so sparklines + pace markers render on the first snapshot post-install instead of after 24 polls. v1 producers still work — the field is additive. ([Shared/SchemaVersion.cs](src/QuotaGlass.Shared/SchemaVersion.cs), [Shared/BucketSnapshot.cs](src/QuotaGlass.Shared/BucketSnapshot.cs), [docs/extension-integration.md](docs/extension-integration.md))
- **AlarmScheduler.BuildActions** — Every bucket-specific toast (R1/R2/R3/U1/U2/U3) carries Snooze 1h + Open Analytics buttons. Argument string format: `action=snooze;bucket=<id>;duration=PT1H` / `action=open;url=https://…`.

### Refactored — moved into Shared so tests can reach them

- **`QuotaGlass.Shared.HistoryStore`** (was `QuotaGlass.Widget.Services.HistoryStore`). Pure persistence on top of `AtomicJsonFile`; no WPF deps.
- **`QuotaGlass.Shared.FiredRulesStore`** (was `QuotaGlass.Widget.Services.FiredRulesStore`). Same — pure persistence.
- **`QuotaGlass.Shared.XmlEscape`** — extracted from `ToastService` so the safety-critical escape logic can be unit-tested without going through WinRT.

### Added — tests

- **6 new `HistoryStoreTests`** — ring-buffer cap, dedupe-by-ts, Flush-after-pending behavior, `MergeIncoming` union, unknown-bucket safety, cross-instance persistence.
- **4 new `FiredRulesStoreTests`** — MarkFired/HasFired round-trip, idempotency, cross-instance persistence, 14-day prune on load.
- **6 new `XmlEscapeTests`** — all five XML 1.0 entities, multibyte passthrough, idempotency note, mixed-content one-pass.
- **1 new `DiagnosticsTests`** — `Diagnostics.Collect` zip contains the four expected entries + orgId/accountId/WAV-path redaction verified.

### Changed

- `Diagnostics` class is now `public` so the unit test can invoke `Collect()` directly.

### Carry-forward / deferred to v0.7

- Settings panel sub-sections + MainWindow.xaml UserControl extraction — substantial XAML refactors; defer until we have CI-verified builds on each PR.
- Multi-account columns full version (R3-P2-01) — needs real multi-account data.
- High-contrast theme + Follow-system-theme.
- Named-pipe NMH↔Widget transport (L-06).
- Localization scaffold.

## [0.5.0] — 2026-05-25

Stabilization release. Closes every P0 / P1 bug Pass 4 ([RESEARCH_PASS_4.md](RESEARCH_PASS_4.md)) found in the v0.1.1..v0.4.0 work, plus 9 quick wins. F-N1 now targets the right endpoints and rotates OAuth tokens; the snapshot pipeline tolerates two producers without flicker; Mica survives a theme switch.

### Fixed

- **R4-P0-01** — F-N1 Claude probe now targets the consumer endpoint (`api.claude.ai/api/organizations/{orgId}/usage`) with the OAuth bearer extracted from `~/.claude/.credentials.json` instead of `api.anthropic.com/v1/messages`. The unified-5h / unified-7d rate-limit headers we parse are only emitted there. Token shape is classified via the new `ClassifyToken` and only OAuth tokens are sent — `sk-ant-…` and `sk-ant-admin-…` keys are surfaced with `detail="unsupported-token-type"` rather than burning tokens on the wrong endpoint. ([NMH/CredentialPoller.cs](src/QuotaGlass.NMH/CredentialPoller.cs))
- **R4-P0-02** — F-N1 Codex probe now dispatches by token shape: `sk-…` OpenAI keys go to `api.openai.com/v1/usage` for daily-total data; ChatGPT browser session tokens are surfaced as `unsupported-token-type` (cookie auth, rejected in Pass 1 Option B). ([NMH/CredentialPoller.cs](src/QuotaGlass.NMH/CredentialPoller.cs))
- **R4-P0-03** — `MicaBackdrop.WasApplied` flag + extracted `ApplyMicaBrushOverride` are now called by `ThemeService.Apply` after every dictionary swap. Mocha↔Latte switches no longer silently re-occlude Mica. ([Services/MicaBackdrop.cs](src/QuotaGlass.Widget/Services/MicaBackdrop.cs), [Services/ThemeService.cs](src/QuotaGlass.Widget/Services/ThemeService.cs))
- **R4-P0-04** — F-N1 no longer sends a real `/v1/messages` "hi" ping per poll. The new consumer endpoint is `GET`-only header-extraction; zero token burn. (Subsumed by R4-P0-01.)
- **R4-P1-01** — `HistoryStore.AppendSample` is in-memory only; new `Flush()` is invoked once per snapshot batch by `MainViewModel.OnSnapshot`. Cuts fsyncs from 4-6 per snapshot to 1. ([Services/HistoryStore.cs](src/QuotaGlass.Widget/Services/HistoryStore.cs), [ViewModels/MainViewModel.cs](src/QuotaGlass.Widget/ViewModels/MainViewModel.cs))
- **R4-P1-02** — `CredentialPoller` writes to a sibling `snapshot.local-creds.json`; `SnapshotWatcher.Merge` reconciles both producers (extension wins on overlap, creds fills gaps). Bucket cards no longer shimmer when both paths run. New `AppPaths.LocalCredsSnapshotFile`. ([Shared/AppPaths.cs](src/QuotaGlass.Shared/AppPaths.cs), [Services/SnapshotWatcher.cs](src/QuotaGlass.Widget/Services/SnapshotWatcher.cs))

### Added

- **R4-N1** — OAuth refresh-token rotation in `CredentialPoller`. On 401 we POST to the refresh endpoint with the stored `refresh_token`, cache the new `access_token` for ≤55 min, retry the probe once. We never write back to the user's `.credentials.json` (the CLI owns it). ([NMH/CredentialPoller.cs](src/QuotaGlass.NMH/CredentialPoller.cs))
- **R4-N4** — `QuotaGlass.NMH.exe --register` now detects Claude Code / Codex / Hermes credential files and registers a per-user Scheduled Task `QuotaGlass.CredentialPoll` (logon trigger + 30-min repetition). `--unregister` removes it. Uses `schtasks.exe` XML — no new NuGet dep, no admin elevation. ([NMH/ScheduledTaskRegistration.cs](src/QuotaGlass.NMH/ScheduledTaskRegistration.cs))
- **R4-Q-06** — Tray context menu reorganized: top level is Show / Hide / Settings… / Window→ / Updates→ / Quit. Window submenu carries Refresh + Reset position; Updates carries Check-for-updates. Top-level menu height halved. ([Services/TrayIconService.cs](src/QuotaGlass.Widget/Services/TrayIconService.cs))
- **R4-Q-11** — Settings panel "Reset settings to defaults" button. Restores alarm ladder / thresholds / theme / etc. via `Settings.CreateDefault()`; preserves Widget.X/Y and HasShownFirstRunToast. Confirmation dialog. ([ViewModels/SettingsPanelViewModel.cs](src/QuotaGlass.Widget/ViewModels/SettingsPanelViewModel.cs), [Views/MainWindow.xaml](src/QuotaGlass.Widget/Views/MainWindow.xaml))

### Changed

- **R4-Q-03** — `docs/extension-integration.md` now has a dedicated "Direct credential reading (`--poll-credentials`)" section documenting token routing, output file, scheduled-task auto-start, and CLI usage. ([docs/extension-integration.md](docs/extension-integration.md))
- **R4-Q-04** — `Logger.Init` / `WidgetLogger.Init` store the log directory only; per-write computes today's path. Cross-midnight runs roll into the next day's file naturally.
- **R4-Q-05** — `MainViewModel.ReducedMotion` subscribes to `SystemParameters.StaticPropertyChanged`; runtime accessibility-preference flips now propagate.
- **R4-Q-07** — `FocusAssist.QueryState` now caches for 3 s. A snapshot with 6 buckets × 6 rule families no longer makes 15+ P/Invokes.
- **R4-Q-08** — When U3 (anomaly) just fired for the current reset window, R3 (zero-state) is suppressed so the user doesn't get a double-toast.
- **R4-Q-09** — Per-bucket "Snooze until reset" now uses the real `NextResetLocal - now` (clamped to ≥5 min) instead of `TimeSpan.FromDays(8)`.
- **R4-Q-01** — README "Run tests" wording updated: 37+ tests across 6 fixture files (was 11).

### Added — tests

- **8 new `CredentialPollerTests`** for the rewritten F-N1 path: `ClassifyToken` (7 inline cases), `ReadCredentialFile` top-level + nested shapes, `ExtractOpenAiPlatformUsage` happy + empty paths.

### Repo hygiene

- ROADMAP.md slimmed down to **pending work only**. Completed items live in this changelog by release. Pass 1/2/3/4 dossiers remain as evidence references.

### Known limitations carried forward to v0.6

- L-04 toast actions (Snooze 1h / Open analytics buttons on the toast itself) — COM activator work; not in v0.5.
- Schema v2 wire-history bundling — needs AI-Usage_Tracker side; deferred.
- Multi-account columns full version (R3-P2-01) — needs F-N1 to land on real multi-account data first.
- Manual screenshots for `assets/screenshots/` — still open.

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
