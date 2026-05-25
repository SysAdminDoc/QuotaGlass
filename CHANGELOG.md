# Changelog

All notable changes to QuotaGlass will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

In progress for v0.2.0 — see [ROADMAP.md](ROADMAP.md) Phase 2.

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
