# Project Research and Feature Plan — Pass 3 (Post-Ship Audit)

**Project:** QuotaGlass · [`W:/repos/QuotaGlass`](.) · v0.1.0 shipped (tag `v0.1.0`, head `100165e`, 2026-05-25).
**Stack:** .NET 9, WPF + WinForms hybrid (widget) + console NMH; xUnit tests; Inno Setup installer; GitHub Releases self-hosted updater.
**Reads-before-this:** [README.md](README.md), [ROADMAP.md](ROADMAP.md), [CLAUDE.md](CLAUDE.md), [CHANGELOG.md](CHANGELOG.md), [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md) (Pass 1), [RESEARCH_PASS_2.md](RESEARCH_PASS_2.md) (Pass 2), [docs/research.md](docs/research.md), [docs/extension-integration.md](docs/extension-integration.md), [docs/bridge-integration.md](docs/bridge-integration.md).
**Scope:** strictly **additive** to Pass 1 + Pass 2. Re-reads every shipped source file against the now-frozen `v0.1.0` artifact, flags real bugs introduced *in implementation*, and re-prioritizes for v0.2.

---

## Executive Summary

v0.1.0 shipped clean: 11 batches landed, `dotnet build` and `dotnet test` green (11/11), no vulnerable packages, multi-arch installer + portable EXEs uploaded to GitHub Releases, AI-Usage_Tracker v0.2.0 bridge shipped concurrently. The architecture chosen by Pass 1/Pass 2 (raw WinRT toasts, hand-rolled NMH framing, FileSystemWatcher fan-out, fire-once `%LOCALAPPDATA%\QuotaGlass\fired-rules.json`) is sound and matches every competitive pattern Pass 2 verified.

But re-reading the shipped code surfaces **five concrete bugs that the planning docs did not catch**, the most consequential being a real notification regression and a real GDI leak:

1. **P0 — [AlarmScheduler.EvaluateProvider:139–152](src/QuotaGlass.Widget/Services/AlarmScheduler.cs#L139-L152)** — the R1 ladder walks biggest-first and `break`s on the first un-fired tier whose `fireAt <= now`. After a cold start or widget restart with multiple tiers' `fireAt` already in the past, the user gets a **stale tier first** (e.g., "Claude resets in 24 hours" when only 5 minutes remain), followed by 5–6 *more* wrong toasts at 15-second intervals as each subsequent un-fired tier is walked. The intent — "fire each tier once per reset window" — is correct; the walk order is wrong.
2. **P0 — [TrayIconService.RenderIcon:126](src/QuotaGlass.Widget/Services/TrayIconService.cs#L126)** — `Icon.FromHandle(bmp.GetHicon())` leaks a Win32 HICON every time `UpdateBadge` repaints. `Icon.Dispose` does **not** call `DestroyIcon` on a handle it doesn't own. Over a workday, GDI handle count for `QuotaGlass.Widget.exe` climbs monotonically; at the per-process 10000-handle limit (Windows default) the widget paints garbage or crashes.
3. **P1 — [MicaBackdrop.TryApply:38–62](src/QuotaGlass.Widget/Services/MicaBackdrop.cs#L38-L62)** — sets `window.Background = Brushes.Transparent` to let Mica show through, but every visible pixel is inside the `WindowChromeBorder` ([Theme/Controls.xaml:13](src/QuotaGlass.Widget/Theme/Controls.xaml#L13)) whose `Background="{StaticResource Brush.Window.Background}"` is `Mocha.Base @ 0.92`. The Mica backdrop is rendered *behind* an opaque chrome border — invisible in practice on Win11 22621+.
4. **P1 — [docs/extension-integration.md](docs/extension-integration.md) wire schema does NOT carry `history`** — Pass 2 §3.7 claimed sparkline data was already in the snapshot envelope and that NX-08 was therefore "free." The schema spec written after Pass 2 (current `docs/extension-integration.md`) omits `state.history`. Sparkline must come from somewhere — either a schema-v2 bump that bundles `history[]` (~1–2 KB per push) or a widget-side ring buffer. **Either way, it's not free.**
5. **P2 — No single-instance lock.** Two widget icons in the tray after a careless double-launch race two FileSystemWatchers, two TopMostEnforcers, two SettingsStore writers (the second silently corrupts `settings.json` because both hold the lock independently).

**Top 10 fresh opportunities (none of these appear in Pass 1 or Pass 2):**

1. **R3-P0-01** — Fix the R1 ladder walk so it fires the **smallest** un-fired tier whose `fireAt` has passed, not the biggest. (~10 LOC) → cures the cold-start barrage and the “you're 5 min away but I'll tell you 24 h first” bug.
2. **R3-P0-02** — Free the HICON in `TrayIconService` via `DestroyIcon` P/Invoke. (~6 LOC)
3. **R3-P0-03** — Apply the Mica backdrop to `WindowChromeBorder` (or thin the chrome border alpha to <0.5 on Win11 22621+ so Mica is visible). (~15 LOC)
4. **R3-P0-04** — `NMH.csproj` has `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` (no arm64). The Inno installer + release.yml *both* publish arm64 NMH; works today only because `--use-current-runtime` is permissive, but a future SDK behavior change would break the arm64 build. Add `win-arm64`. (1 line)
5. **R3-P0-05** — Settings panel doesn't expose alarm-ladder tier toggles, only the comma-separated label. README + brief both promise "each tier independently toggleable." (~40 LOC of XAML CheckBoxes bound to LadderMinutes list)
6. **R3-P1-01** — Single-instance lock via named `Mutex` ("Global\\QuotaGlass.Widget") in `App.OnStartup`. Second instance focuses the first then exits. (~25 LOC)
7. **R3-P1-02** — `--collect-diagnostics` flag on the NMH that zips logs + redacted snapshot + settings into `%TEMP%\quotaglass-diag.zip` for issue reports. (~50 LOC)
8. **R3-P1-03** — Burn-rate "Pace" alarm tier (was P3 in Pass 2 §3.6 option 3). The `PaceCalculator` already produces ETAs; route it through `AlarmScheduler` as a new `U2` rule family. Closes the brief's "burn-rate notification" gap that today only renders silently in the footer. (~30 LOC)
9. **R3-P1-04** — Window-state CI workflow (`.github/workflows/ci.yml`) on push + PR: `dotnet build && dotnet test`. Release-only workflow today means a regressing PR could merge green-on-paper. (~25 LOC)
10. **R3-P1-05** — README's CHANGELOG entry claims "drop in any `.wav`/`.mp3`/`.m4a`" but `SoundPlayer` only plays WAV and `OpenFileDialog` filter restricts to WAV. The CHANGELOG/README are factually wrong on shipped behavior. Fix wording, or pull in `NAudio` 2.x (~30 LOC) to actually support MP3/M4A.

The rest of this report enumerates everything Pass 3 reviewed, every shipped-code bug discovered, and a prioritized v0.2 implementation queue grounded against real file paths and line numbers.

---

## 1. Evidence Reviewed (Pass 3)

### Source-of-truth re-read

Every source file under `src/` and `test/` was re-read against `v0.1.0` (head `100165e`). Files inspected in this pass:

- [src/QuotaGlass.Shared/AppPaths.cs](src/QuotaGlass.Shared/AppPaths.cs) — verified well-known paths under `%LOCALAPPDATA%\QuotaGlass\`.
- [src/QuotaGlass.Shared/AtomicJsonFile.cs](src/QuotaGlass.Shared/AtomicJsonFile.cs) — `Flush(true)` before rename confirmed; `File.ReadAllText` on the read path (FileShare semantics).
- [src/QuotaGlass.Shared/BucketSnapshot.cs](src/QuotaGlass.Shared/BucketSnapshot.cs) — `SnapshotJsonContext` source-gen + `MaxDepth = 16`.
- [src/QuotaGlass.Shared/SchemaVersion.cs](src/QuotaGlass.Shared/SchemaVersion.cs) — `[1, 1]` range.
- [src/QuotaGlass.NMH/Program.cs](src/QuotaGlass.NMH/Program.cs) — entry-point dispatch + `--purge` body.
- [src/QuotaGlass.NMH/MessagePump.cs](src/QuotaGlass.NMH/MessagePump.cs) — 4-byte LE framing, max-depth detection, ack envelope.
- [src/QuotaGlass.NMH/AllowedOrigins.cs](src/QuotaGlass.NMH/AllowedOrigins.cs) — Chrome ID `olkdpcileldmdemjbiklkhompnhkhjeh` pinned.
- [src/QuotaGlass.NMH/HostRegistrar.cs](src/QuotaGlass.NMH/HostRegistrar.cs) — manifest writer uses anonymous types + `JsonSerializer` (reflection path).
- [src/QuotaGlass.NMH/HostMetadata.cs](src/QuotaGlass.NMH/HostMetadata.cs) — version extraction.
- [src/QuotaGlass.NMH/Logger.cs](src/QuotaGlass.NMH/Logger.cs) — rotate @ 10 MB, prune > 14 days.
- [src/QuotaGlass.Widget/App.xaml{.cs}](src/QuotaGlass.Widget/App.xaml.cs) — startup, dispatcher exception capture.
- [src/QuotaGlass.Widget/Views/MainWindow.xaml{.cs}](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs) — tray wiring, position persistence, URL safety.
- [src/QuotaGlass.Widget/Controls/RadialRing.cs](src/QuotaGlass.Widget/Controls/RadialRing.cs) — 60/85 ramp, ReducedMotion DP (currently a no-op).
- [src/QuotaGlass.Widget/Theme/CatppuccinMocha.xaml](src/QuotaGlass.Widget/Theme/CatppuccinMocha.xaml) + [Controls.xaml](src/QuotaGlass.Widget/Theme/Controls.xaml) — palette + control templates.
- [src/QuotaGlass.Widget/ViewModels/{Main,Bucket,SetupCard,SettingsPanel}ViewModel.cs](src/QuotaGlass.Widget/ViewModels) — INotifyPropertyChanged surface.
- [src/QuotaGlass.Widget/Services/AlarmScheduler.cs](src/QuotaGlass.Widget/Services/AlarmScheduler.cs) — R1/R2/R3/U1 ladder evaluator.
- [src/QuotaGlass.Widget/Services/ToastService.cs](src/QuotaGlass.Widget/Services/ToastService.cs) — raw `Windows.UI.Notifications`, `SoundPlayer` fallback.
- [src/QuotaGlass.Widget/Services/TopMostEnforcer.cs](src/QuotaGlass.Widget/Services/TopMostEnforcer.cs) — WinEvent foreground hook on STA thread.
- [src/QuotaGlass.Widget/Services/TrayIconService.cs](src/QuotaGlass.Widget/Services/TrayIconService.cs) — runtime-rendered NotifyIcon.
- [src/QuotaGlass.Widget/Services/SnapshotWatcher.cs](src/QuotaGlass.Widget/Services/SnapshotWatcher.cs) — 250 ms debounce.
- [src/QuotaGlass.Widget/Services/SettingsStore.cs](src/QuotaGlass.Widget/Services/SettingsStore.cs) — atomic persistence, `Changed` event.
- [src/QuotaGlass.Widget/Services/FiredRulesStore.cs](src/QuotaGlass.Widget/Services/FiredRulesStore.cs) — 14-day prune-on-load.
- [src/QuotaGlass.Widget/Services/AutostartRegistration.cs](src/QuotaGlass.Widget/Services/AutostartRegistration.cs) — `HKCU\…\Run` write.
- [src/QuotaGlass.Widget/Services/HealthCheck.cs](src/QuotaGlass.Widget/Services/HealthCheck.cs) — three preconditions.
- [src/QuotaGlass.Widget/Services/MicaBackdrop.cs](src/QuotaGlass.Widget/Services/MicaBackdrop.cs) — DWM attribute application.
- [src/QuotaGlass.Widget/Services/PaceCalculator.cs](src/QuotaGlass.Widget/Services/PaceCalculator.cs) — 2-sample linear extrapolation.
- [src/QuotaGlass.Widget/Services/UpdateChecker.cs](src/QuotaGlass.Widget/Services/UpdateChecker.cs) — GitHub Releases API + PS1 self-replace.
- [src/QuotaGlass.Widget/Services/FakeSnapshotInjector.cs](src/QuotaGlass.Widget/Services/FakeSnapshotInjector.cs) — deterministic dev snapshot.
- [src/QuotaGlass.Widget/Services/WidgetLogger.cs](src/QuotaGlass.Widget/Services/WidgetLogger.cs) — mirrors NMH logger.
- [test/QuotaGlass.Tests/*.cs](test/QuotaGlass.Tests) — 11 passing tests across 3 files.
- [installer/quotaglass.iss](installer/quotaglass.iss) — Inno Setup config.
- [.github/workflows/release.yml](.github/workflows/release.yml) — multi-arch publish-and-upload.
- [Directory.Build.props](Directory.Build.props) — central version, `TreatWarningsAsErrors=true`.

### Git history reviewed

```
100165e chore: pin AI-Usage_Tracker Chrome ID after upstream v0.2.0 bridge
af72b30 batch 11: phase 2 polish (F-N5, NX-05, NX-07)
9fb3634 batch 10: tests + final docs (F-A16, N-19)
fa58b94 batch 9: logging + observability (F-A10, R-Rec-02, R-Log-03)
7576b54 batch 8: distribution stack (F-N10, R2-P1-06, R2-P1-08, N-17, N-18)
261e489 batch 7: cross-repo bridge — drop-in ready (F-A2, F-A4)
8236531 batch 6: embedded settings panel + persistence (N-15, N-16)
709e546 batch 5: system tray + first-run setup checklist (F-N4, F-N3, F-N10)
5c9d13d batch 4: widget polish (F-N8, F-N6, F-A9, F-A19, R2-P1-03)
cf724a2 batch 3: alarm UX foundation (R2-P0-01, R2-P0-02, R2-P0-03, N-12, N-13, N-14)
7fc3e40 batch 2: schema rewrite (F-N9, F-A1, F-A5, F-A12, F-A14, R2-P1-02)
be03f85 batch 1: quick-win corrections (F-A3, F-A7, F-A13, F-A17, F-A18, F-A20, F-A21)
```

### Build / test / docs / release artifacts inspected

- `dotnet build QuotaGlass.sln -c Release` per CLAUDE.md gotchas — green, no warnings.
- `dotnet test  QuotaGlass.sln -c Release` — 11/11 passing.
- `dotnet list package --vulnerable` — clean (per Pass 2's elimination of `Microsoft.Toolkit.Uwp.Notifications`).
- Release workflow [.github/workflows/release.yml](.github/workflows/release.yml) was triggered on tag `v0.1.0`; artifacts (`-x64.exe`, `-arm64.exe`, installer EXEs) live at `https://github.com/SysAdminDoc/QuotaGlass/releases/tag/v0.1.0`.
- No `assets/screenshots/` populated yet (ROADMAP N-20 still open).

### External sources newly verified (Pass 3-only)

- [System.Drawing.Icon.FromHandle docs](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.icon.fromhandle) — confirmed: "When using this method, you must dispose of the original icon by using the `DestroyIcon` method in the Win32 API to ensure that the resources are released." Verifies R3-P0-02.
- [DwmSetWindowAttribute DWMWA_SYSTEMBACKDROP_TYPE docs](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute) — confirms Mica needs an unobstructed window surface; opaque child elements occlude it. Verifies R3-P0-03.
- [Chrome MV3 service-worker lifecycle docs](https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle) — re-confirmed Pass 1 F-A4's 25 s ping is still inside Chrome's 30 s idle window.
- [Anthropic Admin API usage report — Jan 2026 docs](https://docs.anthropic.com/en/api/admin-api/usage-cost/get-usage-report-messages) — still workspace-scoped (billing only), still no per-user / per-window quota view. Reaffirms Pass 1 F-N1's credential-file source choice.
- [`ryoppippi/ccusage` v2.x release notes](https://github.com/ryoppippi/ccusage/releases) — cadence picked up; new "compact" mode + `--watch` flag. Sister-project surface, not competitive.

### Areas Pass 3 could not verify

- **arm64 widget at runtime.** No arm64 Win11 device in the lab. Build succeeds (CI is green) but visual + alarm + tray behavior is **Needs live validation** before users on Snapdragon-X laptops install v0.1.x.
- **`https://chatgpt.com/codex/cloud/settings/analytics#usage`** is the URL [BucketViewModel.AnalyticsUrl:75](src/QuotaGlass.Widget/ViewModels/BucketViewModel.cs#L75) opens on a Codex card click. ChatGPT URL paths churn — confirm the link still works on a real Codex Plus account.
- **`HICON` leak in `TrayIconService` causes paint-garbage at the 10000-handle limit** — theoretical; have not measured how many `UpdateBadge` invocations it takes to hit the limit. Worst case is ~1 per worst-bucket percent change (so probably ≤ 100 per day).

---

## 2. Current Product Map (post-ship, what actually exists)

### Shipped end-to-end pipeline

```
┌────────────────────────────────────────────────────────────────┐
│ AI-Usage_Tracker v0.2.0 (Chrome/Edge/Firefox extension)        │
│   src/lib/bridge.js — persistent port + 25s ping + reconnect  │
│   manifest "key" pinned → ID olkdpcileldmdemjbiklkhompnhkhjeh │
│   "nativeMessaging" permission                                 │
└────────────────────────────┬───────────────────────────────────┘
                             │ stdin/stdout 4-byte LE + UTF-8 JSON
                             ▼
┌────────────────────────────────────────────────────────────────┐
│ QuotaGlass.NMH.exe (.NET 9 net9.0-windows, ~12 KB)             │
│   --register / --unregister / --purge / --version / --help    │
│   AllowedOrigins.IsAllowed → reject otherwise                  │
│   SnapshotJsonContext source-gen + MaxDepth=16                 │
│   Logger (10 MB rotate, 14 d prune)                            │
│   Ack envelope: ok/detail/kind/nmhVersion/schemaMin/schemaMax  │
└────────────────────────────┬───────────────────────────────────┘
                             │ AtomicJsonFile (fsync + rename)
                             │ %LOCALAPPDATA%\QuotaGlass\snapshot.json
                             ▼
┌────────────────────────────────────────────────────────────────┐
│ QuotaGlass.Widget.exe (.NET 9 net9.0-windows10.0.19041.0)      │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ SnapshotWatcher (250 ms debounce FileSystemWatcher)      │  │
│  │   → MainViewModel.OnSnapshot                             │  │
│  │     → BucketViewModel.Apply (reconcile by Bucket.Id)     │  │
│  │     → PaceCalculator.Forecast (2-sample linear)          │  │
│  │     → AlarmScheduler.OnSnapshot                          │  │
│  │       → ToastService.Show (raw WinRT + SoundPlayer)      │  │
│  │       → FiredRulesStore.MarkFired (14 d retention)       │  │
│  └──────────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ TopMostEnforcer (STA thread + WinEvent foreground hook)  │  │
│  │ TrayIconService (WinForms NotifyIcon + worst-bucket badge│  │
│  │ SetupCardViewModel + HealthCheck (3 preconditions)       │  │
│  │ SettingsStore + SettingsPanelViewModel (atomic JSON)     │  │
│  │ AutostartRegistration (HKCU\…\Run)                       │  │
│  │ MicaBackdrop.TryApply (Win11 22621+)                     │  │
│  │ UpdateChecker (GitHub Releases API + PS1 self-replace)   │  │
│  │ WidgetLogger (mirrors NMH logger pattern)                │  │
│  │ FakeSnapshotInjector (--inject-fake-snapshot)            │  │
│  └──────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────┘
```

### User personas (refined post-Pass-2)

- **Browser-session Claude Pro/Max5/Max20** — still the headline persona uniquely served by QuotaGlass.
- **ChatGPT.com Codex web user** — second-class until the extension's Codex scraping improves; the wire schema supports them today.
- **Mixed browser + Claude Code CLI** — gap remains; closes when F-N1 lands.
- **Surface Pro X / Snapdragon-X arm64 laptop** — installer ships; runtime behavior **Needs live validation**.

### Platforms / distribution / data flows

- **Windows 10 1809+** (`<SupportedOSPlatformVersion>10.0.17763.0`). All `[SupportedOSPlatform]` attributes consistent on services that need Win10+ APIs.
- **Per-user install** via Inno Setup to `%LOCALAPPDATA%\Programs\QuotaGlass\`. No HKLM.
- **GitHub Releases** distribution; Inno Setup installer + single-file portable Widget + portable NMH per arch.
- **Self-hosted updater** (lazy, only runs on demand via [UpdateChecker.CheckAsync](src/QuotaGlass.Widget/Services/UpdateChecker.cs#L52)) — pulls `releases/latest`, PowerShell self-replace script.
- **Outbound network calls:** exactly one — `GET api.github.com/repos/SysAdminDoc/QuotaGlass/releases/latest`. Privacy promise in README still holds.

---

## 3. Feature Inventory (post-ship maturity matrix)

| ID | Feature | Code | Maturity | Tests | Docs | Pass 3 finding |
|---|---|---|---|---|---|---|
| F-01 | NMH wire protocol | [MessagePump.cs](src/QuotaGlass.NMH/MessagePump.cs) | **Complete** | `SnapshotSchemaTests` | extension-integration.md | None new. Solid. |
| F-02 | Atomic JSON write | [AtomicJsonFile.cs](src/QuotaGlass.Shared/AtomicJsonFile.cs) | **Complete** | `AtomicJsonFileTests` | inline | None new. |
| F-03 | Snapshot persistence | [AppPaths.cs](src/QuotaGlass.Shared/AppPaths.cs) | **Complete** | indirect | inline | None. |
| F-04 | HKCU NMH registrar | [HostRegistrar.cs](src/QuotaGlass.NMH/HostRegistrar.cs) | **Complete** | none | inline | Manifest writer is reflection-based JSON; non-AOT (latent risk only). |
| F-05 | NMH CLI (`--register/--unregister/--purge/--version/--help`) | [Program.cs](src/QuotaGlass.NMH/Program.cs) | **Complete** | none | README | None new. Add `--collect-diagnostics` (R3-P1-02). |
| F-06 | NMH file logger | [Logger.cs](src/QuotaGlass.NMH/Logger.cs) | **Complete** | none | inline | None. |
| F-07 | Widget chrome (borderless / draggable / topmost) | [MainWindow.xaml{.cs}](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs) | **Complete** | none | README | No single-instance lock (R3-P1-01). |
| F-08 | Radial-ring countdown | [RadialRing.cs](src/QuotaGlass.Widget/Controls/RadialRing.cs) | **Complete** | none | inline | `ReducedMotion` DP exists but is a no-op until animations land in v0.2. |
| F-09 | Bucket reconciler by `Id` | [MainViewModel.cs](src/QuotaGlass.Widget/ViewModels/MainViewModel.cs) | **Complete** | none | inline | None. |
| F-10 | Live countdown ticker | [BucketViewModel.TickCountdown](src/QuotaGlass.Widget/ViewModels/BucketViewModel.cs#L125) | **Complete** | none | inline | Cached-string optimization landed (Pass 1 F-A17). |
| F-11 | FileSystemWatcher debounce | [SnapshotWatcher.cs](src/QuotaGlass.Widget/Services/SnapshotWatcher.cs) | **Complete** | none | inline | None. |
| F-12 | TopMost enforcement | [TopMostEnforcer.cs](src/QuotaGlass.Widget/Services/TopMostEnforcer.cs) | **Complete** | none | inline | Pause/Resume is a state flag, never wired into anything (no caller). Future-use only. |
| F-13 | Toast notifications | [ToastService.cs](src/QuotaGlass.Widget/Services/ToastService.cs) | **Complete** | none | inline | None. AUMID hard-coded; installer matches. |
| F-14 | Alarm scheduler | [AlarmScheduler.cs](src/QuotaGlass.Widget/Services/AlarmScheduler.cs) | **Partial / buggy** | none | inline | **R1 walk order is wrong on cold-start** (R3-P0-01). R2 gate `prevPercent > 0 && bucket.PercentUsed < 10` (line 108) drops valid renewals if user burns >10 % in the first minute post-reset. |
| F-15 | Fire-once persistence | [FiredRulesStore.cs](src/QuotaGlass.Widget/Services/FiredRulesStore.cs) | **Complete** | none | inline | `Save()` does not catch IOException — atomic write throws bubble to `MarkFired` callers which swallow nothing. Wrap in try/catch + log. |
| F-16 | Tray icon + badge | [TrayIconService.cs](src/QuotaGlass.Widget/Services/TrayIconService.cs) | **Buggy** | none | inline | **HICON leak** (R3-P0-02). |
| F-17 | Setup checklist | [SetupCardViewModel.cs](src/QuotaGlass.Widget/ViewModels/SetupCardViewModel.cs) + [HealthCheck.cs](src/QuotaGlass.Widget/Services/HealthCheck.cs) | **Complete** | none | inline | The "Run --register" button assumes `QuotaGlass.NMH.exe` sits next to the widget EXE. True for installer; false for `dotnet run`. Acceptable. |
| F-18 | Settings persistence | [SettingsStore.cs](src/QuotaGlass.Widget/Services/SettingsStore.cs) | **Complete** | none | inline | None. |
| F-19 | Settings panel UI | [MainWindow.xaml:167-230](src/QuotaGlass.Widget/Views/MainWindow.xaml#L167-L230) + [SettingsPanelViewModel.cs](src/QuotaGlass.Widget/ViewModels/SettingsPanelViewModel.cs) | **Partial** | none | none | **Ladder tiers are display-only** (line 186: `Reset ladder: {LadderLabel}`). README + brief promise per-tier toggle. R3-P0-05. WAV picker filter is `.wav` only — matches actual `SoundPlayer` capability, but CHANGELOG/README say MP3/M4A are supported. R3-P1-05. |
| F-20 | Autostart registration | [AutostartRegistration.cs](src/QuotaGlass.Widget/Services/AutostartRegistration.cs) | **Complete** | none | inline | None. |
| F-21 | Mica/Acrylic backdrop | [MicaBackdrop.cs](src/QuotaGlass.Widget/Services/MicaBackdrop.cs) | **Broken visually** | none | inline | **Backdrop hidden by opaque child border** (R3-P0-03). |
| F-22 | Stale-snapshot dimming | [MainViewModel.UpdateStaleness](src/QuotaGlass.Widget/ViewModels/MainViewModel.cs#L114) | **Complete** | none | inline | None. |
| F-23 | Bucket card → analytics URL | [MainWindow.xaml.cs OnCardClicked](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs#L141) | **Complete** | none | inline | URL scheme guarded to http(s) only. Codex URL needs live re-confirmation. |
| F-24 | Position persistence | [MainWindow.xaml.cs OnSourceInitialized](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs#L71) | **Complete** | none | inline | None. |
| F-25 | Pace footer | [PaceCalculator.cs](src/QuotaGlass.Widget/Services/PaceCalculator.cs) | **Complete (silent)** | none | inline | Pace label renders but **never feeds an alarm**. R3-P1-03. |
| F-26 | Self-hosted updater | [UpdateChecker.cs](src/QuotaGlass.Widget/Services/UpdateChecker.cs) | **Complete (lazy)** | none | inline | No automatic check — needs UI affordance ("Check for updates" tray menu item). |
| F-27 | Inno installer | [installer/quotaglass.iss](installer/quotaglass.iss) | **Complete** | none | README | None. |
| F-28 | Release workflow | [.github/workflows/release.yml](.github/workflows/release.yml) | **Complete (manual)** | implicit | inline | No CI workflow on push/PR (R3-P1-04). |
| F-29 | Widget logger | [WidgetLogger.cs](src/QuotaGlass.Widget/Services/WidgetLogger.cs) | **Complete** | none | inline | Same midnight-rollover quirk as NMH logger (path pinned at Init). Cosmetic. |
| F-30 | Fake snapshot dev mode | [FakeSnapshotInjector.cs](src/QuotaGlass.Widget/Services/FakeSnapshotInjector.cs) | **Complete** | none | README | None. |

---

## 4. Bugs Found in v0.1.0 Shipped Code (Pass 3 only)

### Bug 1 — R1 ladder walk fires the WRONG tier on cold start (P0)

**File:** [src/QuotaGlass.Widget/Services/AlarmScheduler.cs:134-153](src/QuotaGlass.Widget/Services/AlarmScheduler.cs#L134-L153)

```csharp
foreach (var lead in Ladder)         // [24h, 12h, 6h, 3h, 1h, 30m, 15m, 5m, 0]
{
    var fireAt = resetAt - lead;
    if (now < fireAt) continue;
    if (now > resetAt + TimeSpan.FromMinutes(2)) continue;
    var key = $"{providerKey}-{bucket.Id}-R1-{FormatLead(lead)}-{Iso(bucket.ResetIso)}";
    if (_fired.HasFired(key)) continue;
    ...
    FireOnce(key, title, body, CustomWavPath);
    break;
}
```

**Scenario:** widget launches at `T-5min`. `_fired` is empty. Walk begins at `24h`; `fireAt = resetAt - 24h`, `now > fireAt` (way past). The fire-window guard `now > resetAt + 2min` is false (we're still 5 min before reset). Key not yet fired. **Fires "Claude resets in 24 hours" toast.** Break exits.

15 seconds later, scheduler ticks again. `24h` is now fired. Walks to `12h` — same logic — fires "Claude resets in 12 hours." Repeat for `6h`, `3h`, `1h`, `30m`, `15m`, and finally the actually-correct `5m`. **User sees 7 wrong toasts in 90 seconds.**

**Verified behavior** by reading the code; not yet reproduced live. Symptom would also surface when a user laptop wakes from sleep with an outstanding ladder.

**Fix shape:**

```csharp
// Walk smallest-first; fire the smallest un-fired tier whose fireAt has
// passed but whose reset window has NOT yet elapsed. Mark all "missed"
// larger tiers as fired so they never replay stale.
TimeSpan? chosen = null;
foreach (var lead in Ladder.OrderBy(t => t))   // 0, 5m, 15m, …, 24h
{
    var fireAt = resetAt - lead;
    if (now < fireAt) break;                    // tier hasn't elapsed yet
    if (now > resetAt + TimeSpan.FromMinutes(2)) continue; // window past
    var key = $"{providerKey}-{bucket.Id}-R1-{FormatLead(lead)}-{Iso(bucket.ResetIso)}";
    if (_fired.HasFired(key)) continue;
    chosen = lead;
    break;
}
if (chosen.HasValue) { /* fire & mark fired */ }

// Suppress all earlier (larger-lead) tiers for this reset window so we
// don't backfire after the user's cold start.
foreach (var lead in Ladder.Where(t => t > chosen.GetValueOrDefault(TimeSpan.MaxValue)))
{
    var fireAt = resetAt - lead;
    if (now > fireAt && !_fired.HasFired(key)) _fired.MarkFired(key);
}
```

**Test:** add an `AlarmSchedulerColdStartTests` xUnit test that injects a snapshot with `resetIso = now + 5min` and an empty `FiredRulesStore`, ticks the scheduler, asserts: exactly one toast fired (the `5m` tier), and the `24h..15m` tiers' keys are all marked fired.

---

### Bug 2 — HICON GDI leak in `TrayIconService.UpdateBadge` (P0)

**File:** [src/QuotaGlass.Widget/Services/TrayIconService.cs:60-66, 95-127](src/QuotaGlass.Widget/Services/TrayIconService.cs#L60-L66)

```csharp
public void UpdateBadge(double worstPercent)
{
    if (Math.Abs(_badgePercent - worstPercent) < 0.5) return;
    _badgePercent = worstPercent;
    _icon.Icon?.Dispose();           // ← does NOT call DestroyIcon on a FromHandle()-wrapped icon
    _icon.Icon = RenderIcon(_badgePercent);
    _icon.Text = $"QuotaGlass — worst bucket {worstPercent:0}%";
}

private static Icon RenderIcon(double percent)
{
    ...
    return Icon.FromHandle(bmp.GetHicon());   // ← leaks one HICON per call
}
```

Per [Icon.FromHandle docs](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.icon.fromhandle), the `Icon` returned does not take ownership of the HICON. `Icon.Dispose` releases the managed wrapper but **leaves the Win32 HICON in the user-object table**. Each call to `UpdateBadge` leaks one HICON; default per-process limit is 10,000 user objects. The widget will paint garbage or crash long before then on a busy machine, but the slow leak is real.

**Fix shape:**

```csharp
[DllImport("user32.dll", SetLastError = true)]
private static extern bool DestroyIcon(IntPtr hIcon);

private IntPtr _hIcon = IntPtr.Zero;

private void SetIcon(Icon newIcon, IntPtr newHandle)
{
    _icon.Icon = newIcon;                  // assign first so NotifyIcon retains a ref
    if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
    _hIcon = newHandle;
}

private static (Icon, IntPtr) RenderIcon(double percent)
{
    ...
    var handle = bmp.GetHicon();
    return (Icon.FromHandle(handle), handle);
}
```

Also call `DestroyIcon(_hIcon)` in `Dispose`.

**Test:** add an integration test that calls `UpdateBadge` 1000 times with alternating values; assert `GetGuiResources(handle, GR_USEROBJECTS)` does not grow unbounded. (Optional; manual GDI handle inspection via Task Manager → Details → User Objects column suffices for verification.)

---

### Bug 3 — Mica backdrop hidden behind opaque chrome border (P1)

**Files:** [src/QuotaGlass.Widget/Services/MicaBackdrop.cs:55](src/QuotaGlass.Widget/Services/MicaBackdrop.cs#L55), [src/QuotaGlass.Widget/Theme/Controls.xaml:13-19](src/QuotaGlass.Widget/Theme/Controls.xaml#L13-L19), [src/QuotaGlass.Widget/Theme/CatppuccinMocha.xaml:34](src/QuotaGlass.Widget/Theme/CatppuccinMocha.xaml#L34)

`MicaBackdrop.TryApply` correctly sets `window.Background = Brushes.Transparent`. But the visible window content is a `<Border Style="{StaticResource WindowChromeBorder}">` whose Background is `Brush.Window.Background` (`Mocha.Base @ 0.92`). 92% opacity on a solid color is essentially opaque to the Mica composition layer.

**Verified by reading the XAML stack.** On Win11 22621+, the DWM applies Mica to the window's outermost frame, but the inner border paints on top with Mocha.Base at 92% — the user sees the same Mocha.Base shade they saw on Win10, with maybe a hint of wallpaper bleed on a 1-pixel edge.

**Fix shape:**

Option A (recommended): when Mica is applied, swap `WindowChromeBorder.Background` to a much-thinner Mocha tint (e.g. `Mocha.Base @ 0.35`) so Mica is visible through it. Bind via theme dictionary swap or `MicaBackdrop.TryApply` setting an attached property.

Option B: paint the chrome border as `Transparent` and rely entirely on Mica. Loses Catppuccin identity on Win10.

**Verification:** screenshot the widget on Win11 23H2 with a contrasting wallpaper before and after the fix; Mica should pick up wallpaper colors after.

---

### Bug 4 — Pass 2 §3.7 sparkline claim is wrong; schema does not include `history` (P1)

**Files:** [docs/extension-integration.md](docs/extension-integration.md) §"Inbound messages — kind: snapshot", [src/QuotaGlass.Shared/BucketSnapshot.cs](src/QuotaGlass.Shared/BucketSnapshot.cs).

Pass 2 §3.7 wrote: "the snapshot envelope contains the raw `history` array; the widget calls `sparklineFor`-equivalent on it." Re-reading the canonical schema spec at `docs/extension-integration.md`, the inbound envelope has only `state.fetchedAtISO + state.providers`. No `history` array. The `BucketSnapshot.cs` C# types match the doc. **The extension-side bridge does not currently push history.**

Two paths to recover the sparkline feature:

1. **Schema v2 — bundle `history`.** Add `state.history: { [bucketId]: [{ts, percentUsed}, …last 24] }`. Bumps `SchemaVersion.Max = 2`; ack carries both; renames the doc; widget gets free sparklines after the AI-Usage_Tracker side also pushes them. ~1–2 KB extra per frame; well under 1 MB cap.
2. **Widget-side ring buffer.** Maintain `(bucketId, ts, percentUsed)[]` in `%LOCALAPPDATA%\QuotaGlass\history.json`, capped at 24 × per bucket. Survives widget restart; data starts fresh after install. Doesn't require schema bump.

**Recommendation:** path 2 for v0.2 (no cross-repo dependency); promote to schema v2 in v0.3 if we want to share history.

---

### Bug 5 — No single-instance lock; double-launch races (P2)

**Symptom:** double-clicking the desktop shortcut twice → two `QuotaGlass.Widget.exe` processes, each:

- Creating their own `FileSystemWatcher` → two SnapshotWatcher reload events per snapshot push.
- Two `SettingsStore` instances → both call `AtomicJsonFile.Write` on the same path; lost-update risk if both users change a checkbox in close succession.
- Two `TrayIconService` instances → two tray icons (user can't tell them apart).
- Two `TopMostEnforcer` STA threads → no functional conflict (both re-assert HWND_TOPMOST on their respective windows).

**Fix shape (R3-P1-01):**

```csharp
// In App.OnStartup, before any service init:
private static Mutex? _instanceMutex;
protected override void OnStartup(StartupEventArgs e)
{
    _instanceMutex = new Mutex(initiallyOwned: true,
                                "Global\\QuotaGlass.Widget", out var owned);
    if (!owned)
    {
        // Send focus-existing-window IPC, or just exit.
        FocusExisting();
        Shutdown();
        return;
    }
    base.OnStartup(e);
}
```

`FocusExisting` can use `FindWindow("QuotaGlass", null)` + `SetForegroundWindow`.

---

### Smaller defects (P2/P3)

- **[AlarmScheduler.EvaluateProvider:108](src/QuotaGlass.Widget/Services/AlarmScheduler.cs#L108) R2 gate too tight.** `prevPercent > 0 && bucket.PercentUsed < 10` drops legit renewals if the user is mid-burst right after reset. Loosen to `prevPercent > 0` alone, or `prevPercent > 50 && bucket.PercentUsed < prevPercent` (detect drop, not absolute level).
- **[MainViewModel.ReducedMotion:46-48](src/QuotaGlass.Widget/ViewModels/MainViewModel.cs#L46-L48)** is a computed property with no `PropertyChanged` raise — if the user changes Windows animation settings at runtime, the widget never picks it up. The DP that consumes it ([RadialRing.ReducedMotion](src/QuotaGlass.Widget/Controls/RadialRing.cs#L106)) is a no-op anyway, so cosmetic.
- **`NMH.csproj <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`** — release.yml + Inno installer both publish arm64 NMH; works only because `--use-current-runtime` is permissive. Tighten to `win-x64;win-arm64`. (R3-P0-04)
- **[FiredRulesStore.Save:62](src/QuotaGlass.Widget/Services/FiredRulesStore.cs#L62)** doesn't catch IOException. If AtomicJsonFile.Write throws (AV scan, network share quirk), the exception bubbles to `MarkFired` callers. Wrap + log; same pattern as [Logger.Write try/catch](src/QuotaGlass.NMH/Logger.cs#L52-L63).
- **[MainWindow.xaml:215, 222](src/QuotaGlass.Widget/Views/MainWindow.xaml#L215) `UpdateSourceTrigger=LostFocus`** on WarnPercent/DangerPercent TextBoxes — if user types `999`, setter clamps to `99` but the TextBox visually still shows `999` until the next refocus. Add `Binding.UpdateSourceTrigger=PropertyChanged` + raise INPC after clamp, or set `MaxLength="2"` to constrain input shape.
- **`WidgetLogger._path` is pinned at `Init`** and never re-resolves; same for [NMH Logger](src/QuotaGlass.NMH/Logger.cs). A widget running across midnight writes the next day's lines into the previous day's log file. Minor — pruning still works correctly off `LastWriteTime`.
- **[MainWindow.xaml.cs:42-44](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs#L42-L44)** — listens for `_vm.PropertyChanged where e.PropertyName == nameof(MainViewModel.Buckets)`. But `MainViewModel.Buckets` is a get-only property that's never reassigned, so this branch never fires. The `Buckets.CollectionChanged` handler at line 40 is the real path. Dead code.
- **`CONTRIBUTING.md` + `SECURITY.md`** still missing.
- **No `.gitattributes`** — line endings drift risk on Windows-only repo; cheap to add (`* text=auto eol=lf`).

---

## 5. Reliability, Security, Privacy, Data Safety

### Reliability

- **R1 ladder cold-start barrage** (Bug 1 above). Real user impact.
- **HICON leak** (Bug 2). Slow but real.
- **No single-instance lock** (Bug 5). Settings corruption risk.
- **No `CrashOnly` semantics across NMH ↔ widget.** If `snapshot.json` becomes truncated mid-write (AV lock, OneDrive sync interference, blue screen), `AtomicJsonFile.Read` returns `null` and the widget stays at "Waiting for first snapshot from extension…" forever — same as a fresh install. **Recovery affordance:** Setup card already auto-collapses once the snapshot ages back into 24 h freshness. Adequate.
- **NMH crash after `--register` succeeded but the manifest write failed mid-flight** would leave HKCU registry keys pointing at a half-written manifest file. The Chromium browser would try to connect, fail to parse, and the user would see "Native messaging host failed" in the extension. Mitigation: write the manifest first, write the registry key second (already the order in `WriteChromiumManifest` → `WriteRegistryKey`). OK.

### Security

- **Toast XML escaping** in [ToastService.Show:42-57](src/QuotaGlass.Widget/Services/ToastService.cs#L42-L57) hand-rolls `Escape(&, <, >, ")`. Misses `'` (XML allows `&apos;` but is fine in text nodes — actually XML 1.0 doesn't require it). String-interpolating user-derived `title` / `body` is safe as long as those come from extension-controlled paths (they do: bucket Label is from Anthropic/OpenAI API responses, threaded through the extension's scrapers). **No injection risk in practice**, but if v0.2 surfaces user-typed text in a toast (e.g. a "snooze for N minutes" confirmation), revisit.
- **`HostRegistrar.WriteChromiumManifest:84` uses anonymous-typed `JsonSerializer.Serialize`** — reflection path. The values are `HostName`, `HostDescription`, `exePath`, `"stdio"`, `allowedOrigins`. None of these are user-controlled. Defensive-only nit; harmless today.
- **`UpdateChecker.LaunchSelfReplace:115-128`** embeds the download URL and exe paths in a PS1 script via string concatenation. URLs come from GitHub's `releases/latest` response, which we trust. `currentExe` is from `Environment.ProcessPath` (Windows-trusted). `tempExe`/`scriptPath` are fixed `Path.Combine` outputs. **No injection vector.**
- **No cert pinning on `api.github.com`** — relies on Windows trust store. Standard practice; acceptable.
- **Settings panel "Pick…" WAV picker** uses `OpenFileDialog` which returns a path the user explicitly chose. Subsequent `SoundPlayer.Play()` is path-only. No remote-load surface. Safe.
- **NMH origin check** ([MessagePump:73-77](src/QuotaGlass.NMH/MessagePump.cs#L73-L77)) is the right guard. Listed in [AllowedOrigins.cs](src/QuotaGlass.NMH/AllowedOrigins.cs#L18-L26). Chrome ID is pinned post-`v0.2.0` bridge.
- **Snapshot JSON contains `orgId`, `accountId`, `plan`** — moderate-sensitivity. File lives at `%LOCALAPPDATA%\QuotaGlass\snapshot.json`. ACLs default to user-only-readable. OneDrive default scope **excludes** `%LOCALAPPDATA%`, so cloud-leakage is unlikely. README's "nothing leaves your machine" stays accurate.
- **WidgetLogger and NMH Logger lines do NOT contain percentages or bucket labels** — only bucket counts + caller origin. Privacy-safe.

### Privacy

- One outbound call only: `GET api.github.com/repos/SysAdminDoc/QuotaGlass/releases/latest`. Logged in WidgetLogger if it fires. README's claim stands. When F-N1 (direct credential reading) lands in v0.2+, README's privacy section must explicitly enumerate the Anthropic + OpenAI endpoints called.
- No telemetry, no analytics, no remote feed. Matches Pass 2 R2-NG-04.

### Data safety

- AtomicJsonFile.Write uses `Flush(true)` then `File.Replace` ([line 28-37](src/QuotaGlass.Shared/AtomicJsonFile.cs#L25-L38)) — power-cut safe.
- `FiredRulesStore.Save` doesn't catch IOException (defect, P2 above).
- `SettingsStore` is `lock`-guarded against concurrent writers within a process; across processes, the second instance can clobber the first (Bug 5).
- `--purge` removes `%LOCALAPPDATA%\QuotaGlass\*` ([Program.cs:42](src/QuotaGlass.NMH/Program.cs#L42)) — destructive but no confirmation. Acceptable because it's CLI; document explicitly.

---

## 6. UX, Accessibility, Trust

### Onboarding

- Setup card with three checks is solid (ROADMAP F-N3). The "Run --register" button assumes `QuotaGlass.NMH.exe` lives next to the widget — true in the installer flow, false in `dotnet run`. Documented gotcha.
- First-run balloon tip ("QuotaGlass is in your tray") fires from `Loaded` ([MainWindow.xaml.cs:46-51](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs#L46-L51)). Fires **every launch**, not just first-run. Track a `Settings.Widget.HasShownFirstRunToast` boolean to suppress on subsequent launches.

### Empty / loading / error / disabled states

- **Empty state pre-snapshot** is the Setup card. Good.
- **Loading state**: there isn't one — snapshot reads are sync (`File.ReadAllText`) and small (<10 KB), so visible flicker is unlikely.
- **Error state**: provider with `Ok = false` is currently silently dropped; the widget shows N-1 bucket cards. Brief gap: surface "Codex: auth failure — sign in again" instead of just hiding the cards.
- **Disabled state**: Settings panel's "Notification alarms enabled" checkbox disables the scheduler; ring rendering continues. Good.

### Destructive actions

- Tray "Quit QuotaGlass" exits immediately, no confirm. Probably fine — re-launch is one click via Start Menu.
- `--purge` is CLI-only and documented; no in-app destructive button.
- Settings panel has no "Reset to defaults" button (Pass 1 R-Rec-01). Pre-v0.2.

### Settings clarity

- Ladder shown as comma-separated string (`24h, 12h, 6h, 3h, 1h, 30m, 15m, 5m, at-reset`) — informative but not editable. R3-P0-05.
- "Custom sound" path text overflows for long paths (no `TextTrimming` set on that TextBlock). Add `TextTrimming="CharacterEllipsis"`.
- No tooltips on Warn % / Danger % — first-time user has to infer that these are ring color thresholds, not alarm thresholds. Tooltip strings: "Ring turns peach above this %", "Ring turns red above this %".
- No "About" surface — no link to GitHub repo, no version display. The tray icon tooltip says `QuotaGlass — worst bucket N%` (line 65) — never includes version. Add "QuotaGlass v{Version} — worst bucket N%".

### Accessibility

- AutomationProperties are partially wired ([MainWindow.xaml:127-128](src/QuotaGlass.Widget/Views/MainWindow.xaml#L127-L128)) — bucket card has `AutomationProperties.Name="{Binding PercentLabel}"`. Bucket label, reset time, and bucket kind are NOT in the AutomationName — screen-reader user hears just `64% used`. Improve to `{Provider} {Label} {PercentLabel} {ResetAtLabel}`.
- Keyboard navigation is not wired — Tab/Enter doesn't focus or activate cards. Pre-v0.2 (ROADMAP UX-Acc-02).
- High-contrast mode untested; the Catppuccin Mocha palette is hard-coded; on Windows High Contrast, brushes don't switch. v0.3 work (ROADMAP NX-06 Latte covers light mode; high-contrast is separate).
- ReducedMotion plumbing exists but is a no-op (no animations ship yet).

### Microcopy + trust

- Status strip shows `Last update: 9:42 AM` — clear. Stale prefix `STALE — ` works.
- README's "drop in any .wav/.mp3/.m4a" overstates capability — currently WAV-only ([R3-P1-05](#r3-p1-05)).
- CHANGELOG header for v0.1.0 says "pending tag" — already tagged; CHANGELOG needs final-tag bump.

---

## 7. Competitive & Ecosystem Research (Pass 3 deltas)

Pass 2 §6 enumerated six Windows competitors. Pass 3 deltas (re-checked May 2026):

| Competitor | Pass 2 finding | Pass 3 delta |
|---|---|---|
| **Zrnik/claude-usage-windows-taskbar-widget** | v0.2.20 (2026-05-12); WinEvent topmost; raw WinRT toast; PS1 updater; Pace.cs working-day math | No new release. Repo still active; issue queue thin. No threat. |
| **CodeZeno/Claude-Code-Usage-Monitor** | Rust; tray badges; WSL creds | Roadmap mentions Wayland/Linux build — confirmed scope creep AWAY from Windows widget UX. |
| **jens-duttke/usage-monitor-for-claude** | Python pystray+pywebview; shell-command webhooks | v1.16.0 dropped 2026-05-22 — added Discord direct integration. Reinforces the webhook-only design choice in [QuotaGlass F-N7](ROADMAP.md). |
| **psinghmanager/g4-Claw-counter** | Multi-provider + user-editable pricing | No release. Stale. |
| **SlavomirDurej/claude-usage-widget** | Surveyed via search; deep architecture not fetched | Fetched: smaller scope than Zrnik; no notifications; abandoned (no commit in 90 days). |
| **SmartAppsCo/claude-usage-widget** | Surveyed via search | Fetched: confirmed pure-status-bar; closed-source binary distribution. |
| **`ryoppippi/ccusage`** | Node CLI; complementary | v2.x added `--watch` and "compact" output. Sister-project. |
| **Cursor / VS Code "AI usage" status item** (NEW — Pass 3) | n/a | Cursor 0.46 (2026-04) added a sidebar "Codex quota" panel for paid users. **Editor surface — not a desktop widget**, doesn't compete head-on with QuotaGlass's always-on-top mandate. |
| **Anthropic Admin API for usage** (NEW — Pass 3) | n/a (Pass 1 Option D) | Re-checked Jan-2026 docs: still org-billing-scoped, no per-window or per-user view. **Still not a viable QuotaGlass data source.** |

**No new competitive entrant** has shipped a browser-session tracker in the same space. QuotaGlass's "the only Windows tracker for browser-session users" positioning remains unique as of 2026-05-25.

**Pass 3 competitive insight (new):** the gap that *every* competitor has is **multi-account on the SAME provider** (work + personal Claude). Zrnik's `CredentialStore.cs` reads up to six credential files but only routes one to the widget. QuotaGlass could leapfrog by rendering side-by-side **account columns** within a single provider once F-N1 lands — and the extension's `state.providers.{claude,codex}` is already account-scoped via `orgId`/`accountId`. Pre-empts the v0.4+ persona "engineering lead watching their team's burn." See R3-P2-01.

---

## 8. Net-new opportunities (Pass 3 only)

Ordered by the same `R3-P{0,1,2,3}-NN` scheme as Pass 2. Items already on ROADMAP are referenced; this section only adds **net-new** items.

### Phase 0 — must fix before v0.1.1

- **R3-P0-01 — Fix R1 ladder cold-start walk** (Bug 1 above).
  - Why: notification UX is currently wrong on every cold start, sleep wake, or laptop close+open.
  - Touches: [AlarmScheduler.EvaluateProvider:134-153](src/QuotaGlass.Widget/Services/AlarmScheduler.cs#L134-L153). New unit test in `test/QuotaGlass.Tests/AlarmSchedulerTests.cs`.
  - Acceptance: cold-start with `T-5min` + empty FiredRulesStore → one toast at `5m` tier; `24h..15m` keys marked fired (suppressed forever for that resetISO).
  - Verify: `dotnet test` against the new test fixture.
  - Estimated complexity: S.

- **R3-P0-02 — Free the HICON in TrayIconService** (Bug 2).
  - Why: real GDI leak; user-object handle exhaustion on long-running sessions.
  - Touches: [TrayIconService.cs](src/QuotaGlass.Widget/Services/TrayIconService.cs) — add `DestroyIcon` P/Invoke + track `_hIcon` field + free on swap and Dispose.
  - Acceptance: 1000 `UpdateBadge` calls in a unit test stub do not grow `GetGuiResources(_, GR_USEROBJECTS)` more than +1 (the current icon).
  - Verify: instrumented loop + Task Manager Details view.
  - Estimated complexity: XS.

- **R3-P0-03 — Make Mica actually visible on Win11** (Bug 3).
  - Why: F-N5 shipped but the visible effect is nil.
  - Touches: [MicaBackdrop.TryApply:38-62](src/QuotaGlass.Widget/Services/MicaBackdrop.cs), [Theme/Controls.xaml WindowChromeBorder:13-19](src/QuotaGlass.Widget/Theme/Controls.xaml#L13-L19), [Theme/CatppuccinMocha.xaml Brush.Window.Background:34](src/QuotaGlass.Widget/Theme/CatppuccinMocha.xaml#L34) — define a separate `Brush.Window.MicaBackground` (Mocha.Base @ 0.35) and have `MicaBackdrop.TryApply` swap the dictionary key at runtime.
  - Acceptance: on Win11 22621+, widget's chrome background visibly picks up desktop wallpaper.
  - Verify: side-by-side screenshots over a colorful wallpaper.
  - Estimated complexity: S.

- **R3-P0-04 — Add `win-arm64` to NMH RuntimeIdentifiers** (defect above).
  - Why: release.yml + installer publish arm64 NMH; csproj omission is a footgun for future SDK changes.
  - Touches: [src/QuotaGlass.NMH/QuotaGlass.NMH.csproj:8](src/QuotaGlass.NMH/QuotaGlass.NMH.csproj#L8) — `<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>`.
  - Acceptance: `dotnet publish src/QuotaGlass.NMH -r win-arm64 --self-contained false` builds clean.
  - Verify: release.yml arm64 job stays green.
  - Estimated complexity: XS.

- **R3-P0-05 — Settings panel: per-tier alarm-ladder toggles** (defect F-19 above).
  - Why: README and brief promise toggleable tiers. Today user must hand-edit JSON.
  - Touches: [MainWindow.xaml:167-230](src/QuotaGlass.Widget/Views/MainWindow.xaml#L167-L230) (ItemsControl of CheckBoxes over `SettingsPanelViewModel.LadderTiers`), [SettingsPanelViewModel.cs](src/QuotaGlass.Widget/ViewModels/SettingsPanelViewModel.cs) (new `LadderTiers : ObservableCollection<LadderTier>`).
  - Acceptance: uncheck "6h" tier → 6h key marked-fired in `FiredRulesStore` immediately so the next tick can't fire it. Re-check → next reset window picks it up.
  - Verify: manual; F-N8 fake-snapshot to seed reset ~10h away, toggle, observe.
  - Estimated complexity: M.

### Phase 1 — v0.2 polish (drop-in candidates)

- **R3-P1-01 — Single-instance Mutex** (Bug 5).
  - Why: settings corruption risk on double-launch.
  - Touches: [App.xaml.cs:OnStartup](src/QuotaGlass.Widget/App.xaml.cs) — `Global\QuotaGlass.Widget` Mutex; second instance posts a `WM_USER` to the first window's HWND (or calls `SetForegroundWindow`) then `Shutdown()`.
  - Acceptance: double-clicking the desktop shortcut twice → second instance focuses the first and exits.
  - Verify: Task Manager shows only one process.
  - Estimated complexity: S.

- **R3-P1-02 — `QuotaGlass.NMH.exe --collect-diagnostics`** (new feature).
  - Why: user-driven issue reports today require asking users to zip multiple folders by hand. A one-liner CLI fits the existing NMH pattern.
  - Behavior: zips `%LOCALAPPDATA%\QuotaGlass\logs\*`, `snapshot.json` (with `orgId`/`accountId` redacted), `settings.json` (with `customWavPath` redacted), and a `meta.txt` (Windows version, app version, NMH registry presence) into `%TEMP%\quotaglass-diag-{ts}.zip`.
  - Touches: new `src/QuotaGlass.NMH/Diagnostics.cs`. [Program.cs](src/QuotaGlass.NMH/Program.cs#L7-L24) gains a `--collect-diagnostics` case.
  - Acceptance: invoking the flag prints the resulting zip path and exits 0.
  - Verify: manual invocation + zip inspection.
  - Estimated complexity: M.

- **R3-P1-03 — Pace alarm tier (route PaceCalculator output through AlarmScheduler)** (new alarm family).
  - Why: brief promises burn-rate notifications; today Pace only renders in the silent footer.
  - Touches: [AlarmScheduler.cs](src/QuotaGlass.Widget/Services/AlarmScheduler.cs) — new `U2-pace` rule keyed `<provider>-<bucket>-U2-<resetISO>`; fires when `PaceCalculator.Forecast` returns an ETA more than 1× the user's "lead minutes" before the next R1 tier would normally fire. Settings field `alarms.paceEnabled` (default ON).
  - Acceptance: fake snapshot with monotonically-burning Claude weekly bucket → after 2 ticks with growing percent, Pace toast fires once.
  - Verify: unit test on `AlarmScheduler` with synthetic samples; manual via FakeSnapshotInjector V2.
  - Estimated complexity: M.

- **R3-P1-04 — CI workflow on push + PR** (`.github/workflows/ci.yml`) (new infra).
  - Why: regressing PR could merge today; release.yml only runs on manual dispatch.
  - Touches: new `.github/workflows/ci.yml` — checkout, setup .NET 9, restore, `dotnet build --no-restore -c Release`, `dotnet test --no-build -c Release`. Trigger: `push` to any branch + `pull_request`. Concurrency group on ref.
  - Acceptance: branch with intentional test failure is blocked from merging via the workflow check.
  - Verify: run the workflow against `main` and against a failure-injection branch.
  - Estimated complexity: S.

- **R3-P1-05 — Fix CHANGELOG/README false-promise re: MP3/M4A** (defect F-19 above).
  - Option A (truthful, S): change README + CHANGELOG to say "drop in any **WAV** (MP3/M4A planned)". Update WAV picker filter is already WAV-only — keep as-is.
  - Option B (deliver, M): pull in `NAudio` 2.x (MIT, no native deps), branch on file extension in `ToastService.PlayWav` to either `SoundPlayer` (WAV) or `WaveOutEvent + AudioFileReader` (MP3/M4A). Widen OpenFileDialog filter.
  - Recommendation: Option A for v0.1.1 (CHANGELOG can be corrected post-tag via a `[Yanked]` note or a v0.1.1 entry). Option B for v0.2.
  - Estimated complexity: S (Option A) / M (Option B).

- **R3-P1-06 — First-run toast suppression after one successful run** (defect §6 above).
  - Why: current code fires `_tray.NotifyFirstRun()` every launch.
  - Touches: [SettingsStore.Settings.Widget](src/QuotaGlass.Widget/Services/SettingsStore.cs) — add `HasShownFirstRunToast : bool`. [MainWindow.xaml.cs:49](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs#L49) — gate on the flag, set it on success.
  - Acceptance: install, launch, see toast; quit + relaunch within 1 min → no toast.
  - Verify: manual; pair with a `--reset-first-run` debug flag for QA.
  - Estimated complexity: XS.

- **R3-P1-07 — Setup card collapsible state persists across launches** (UX).
  - Why: today the card re-expands after every launch when any precondition is unmet — fine for first-run, annoying if the user knows they're between snapshot ticks.
  - Touches: [SetupCardViewModel.cs](src/QuotaGlass.Widget/ViewModels/SetupCardViewModel.cs) — add `Dismissed : bool` setting; if user dismisses, hide for 24 h, then re-evaluate.
  - Estimated complexity: S.

- **R3-P1-08 — `Settings → Check for updates` tray menu entry** wiring UpdateChecker (defect F-26 above).
  - Why: shipped Updater is lazy with no UI affordance. Users won't discover updates.
  - Touches: [TrayIconService.cs](src/QuotaGlass.Widget/Services/TrayIconService.cs) — new menu item. [MainWindow.xaml.cs](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs) handler — async call to `UpdateChecker.CheckAsync`, on `UpdateInfo` show a confirm dialog → `LaunchSelfReplace`.
  - Acceptance: tag a no-op `v0.1.1` Release; v0.1.0 user clicks tray "Check for updates" → modal → restart as v0.1.1.
  - Verify: manual against a local Release.
  - Estimated complexity: S.

### Phase 2 — v0.3 differentiators

- **R3-P2-01 — Multi-account columns within a provider.** Folds into ROADMAP F-N1 (direct credential reading) — once that lands, the snapshot envelope contains multiple `orgId`/`accountId` records, render side-by-side. **Net-new competitive moat.** Estimated complexity: L.
- **R3-P2-02 — Widget-side history ring buffer** for NX-08 sparklines (Bug 4 mitigation path). New `Services/HistoryStore.cs` — durable `%LOCALAPPDATA%\QuotaGlass\history.json`, capped 24 samples × N buckets. M.
- **R3-P2-03 — Schema v2 bundle `history` from the extension** (alternate path for sparklines). Bump `SchemaVersion.Max`. L (cross-repo).
- **R3-P2-04 — Focus-Assist awareness.** Suppress non-priority toasts during `QUNS_QUIET_TIME` / `QUNS_PRESENTATION_MODE` per [SHQueryUserNotificationState](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryusernotificationstate). Defer to AlarmScheduler ladder. M.
- **R3-P2-05 — DPI/scale-aware ring rendering.** Today the inner `TimeUntilResetLabel` overflows the 80-px ring at ≥200 % scale on a 4K display. Use `ViewBox` or DP-bound font size. S.
- **R3-P2-06 — Per-bucket mute/snooze.** Right-click a card → "Hide this bucket for {1h, 6h, 24h, until reset}." Quick competitive parity. S.
- **R3-P2-07 — Reset position tray-menu entry** (defect §6 above). Resets `Widget.X/Y` to `(40, 40)`. XS.

### Phase 3 — v0.4+

- **R3-P3-01 — Localization scaffolding.** Move strings to `Resources.resx`. L (touches every UI string).
- **R3-P3-02 — Win11 Widgets board integration** (ROADMAP L-03). XL; needs adaptive cards research.
- **R3-P3-03 — Provider plugin contract** (ROADMAP L-10). Out-of-process DLLs or in-process strategy interfaces — needs design pass.
- **R3-P3-04 — Avalonia port** for cross-platform (ROADMAP UC-01). XL.

---

## 9. Quick wins (Pass 3 only, ≤30 min each)

1. **R3-P0-04** — `<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>` in NMH csproj. 1 line.
2. **R3-P1-05 Option A** — CHANGELOG/README wording correction re: MP3/M4A. Paragraph rewrite.
3. **R3-P0-02** — DestroyIcon P/Invoke in TrayIconService. ~6 LOC.
4. **R3-P1-06** — `HasShownFirstRunToast` flag for the tray balloon. ~10 LOC.
5. **R3-P1-04** — Add `.github/workflows/ci.yml` (build + test on push/PR). ~25 LOC YAML.
6. **Add `.gitattributes`** with `* text=auto eol=lf`. 1 line.
7. **Add `SECURITY.md`** with a simple "Report via GitHub Security Advisories" line. ~20 LOC.
8. **Tooltip text** on Warn % / Danger % TextBoxes ([MainWindow.xaml:213-226](src/QuotaGlass.Widget/Views/MainWindow.xaml#L213-L226)). 4 lines.
9. **`AutomationProperties.Name`** expansion on bucket card (Pass 1 UX-Acc-01 partially landed; widen to include provider + reset). 1 line edit.
10. **Tray icon tooltip** include version: `_icon.Text = $"QuotaGlass v{HostMetadata.Version} — worst bucket {worstPercent:0}%"`. 1 line.

---

## 10. Larger Bets (Pass 3 only)

1. **R3-P0-01 — Fix the R1 ladder walk + add a regression test suite.** Touches the alarm scheduler, requires careful idempotency reasoning for the "suppress earlier tiers" branch.
2. **R3-P0-05 — Per-tier toggle UI** for the alarm ladder. ItemsControl-of-CheckBoxes inside the settings panel; binds to a new `SettingsPanelViewModel.LadderTiers` collection; round-trips through `SettingsStore.Update`.
3. **R3-P1-02 — Diagnostic-collection CLI** with redaction. Forces clear thinking about what's "sensitive" in our data model (orgId, accountId, paths).
4. **R3-P1-03 — Pace alarm tier.** Needs hysteresis ("don't re-fire when the user's pace fluctuates ±5 %"). Settings field exposure.
5. **R3-P2-01 — Multi-account columns.** Bigger UI change; ties into Pass 1 F-N1's credential-file source.

---

## 11. Explicit Non-Goals (Pass 3 only — additional)

- **Do NOT migrate to NAudio for full audio format support in v0.1.1.** Ship the CHANGELOG/README wording correction first; add NAudio only if/when it's part of a planned v0.2 audio overhaul (per-tier sounds, snooze sound, custom alarm preview).
- **Do NOT replace the lazy UpdateChecker with on-launch auto-check.** Privacy ethos says no outbound calls by default. Manual tray-menu trigger (R3-P1-08) is the right model.
- **Do NOT add toast actions (Snooze / Open) in v0.1.1.** ROADMAP L-04 already parks it for v0.3; needs COM activator decision.
- **Do NOT bundle history into the wire schema (schema v2) in v0.2.** Widget-side ring buffer is simpler and doesn't require cross-repo coordination.

---

## 12. Open Questions

These block correct prioritization; everything else has a recommended default.

1. **Does v0.1.1 ship purely as bug-fix (R3-P0-01..05) or fold in R3-P1-* polish?** Default: **bug-fix only**, ship within 5 days, then begin v0.2 work. Confirm.

2. **Sparkline path:** widget ring buffer (R3-P2-02) or schema v2 (R3-P2-03)? Default: **widget ring buffer**, simpler, no cross-repo bump. Confirm.

3. **`NAudio` integration for MP3/M4A:** ship now or document as planned? Default: **document as planned for v0.2**; correct the CHANGELOG wording in v0.1.1. Confirm.

4. **First-run-toast persistence key:** add to Settings.Widget or a separate `FirstRunState` file? Default: `Settings.Widget.HasShownFirstRunToast` (one file fewer; same atomic semantics). Confirm.

---

## 13. Implementation Order (Pass 3 recommendation)

Drop-in queue for the next implementing agent. Each item is checkbox-formatted to paste into ROADMAP.md.

### v0.1.1 — bug-fix point release

- [ ] **P0 — R3-P0-01** — Fix R1 ladder walk order (cold-start barrage + wrong-tier bug).
  - Touches: [AlarmScheduler.EvaluateProvider](src/QuotaGlass.Widget/Services/AlarmScheduler.cs#L134-L153). New `test/QuotaGlass.Tests/AlarmSchedulerTests.cs`.
  - Acceptance: cold-start with `T-5min` empty FiredRulesStore → exactly one toast (`5m` tier); `24h..15m` keys marked fired.
  - Verify: `dotnet test`.
- [ ] **P0 — R3-P0-02** — Free HICON in TrayIconService (DestroyIcon P/Invoke).
  - Touches: [TrayIconService.cs](src/QuotaGlass.Widget/Services/TrayIconService.cs).
  - Acceptance: 1000 UpdateBadge calls don't grow user-object count.
  - Verify: manual via Task Manager Details column.
- [ ] **P0 — R3-P0-03** — Make Mica visible (swap `WindowChromeBorder` background on Win11 22621+).
  - Touches: [MicaBackdrop.TryApply](src/QuotaGlass.Widget/Services/MicaBackdrop.cs#L38), [Theme/Controls.xaml WindowChromeBorder](src/QuotaGlass.Widget/Theme/Controls.xaml#L13).
  - Acceptance: side-by-side screenshots show Mica picking up wallpaper.
  - Verify: manual on Win11 23H2.
- [ ] **P0 — R3-P0-04** — `win-arm64` in NMH csproj RuntimeIdentifiers.
  - Touches: [QuotaGlass.NMH.csproj:8](src/QuotaGlass.NMH/QuotaGlass.NMH.csproj#L8).
  - Acceptance: arm64 publish builds clean locally.
  - Verify: `dotnet publish src/QuotaGlass.NMH -r win-arm64 -c Release --self-contained false`.
- [ ] **P0 — R3-P0-05** — Per-tier alarm-ladder toggles in settings panel.
  - Touches: [MainWindow.xaml settings section](src/QuotaGlass.Widget/Views/MainWindow.xaml#L167-L230), [SettingsPanelViewModel.cs](src/QuotaGlass.Widget/ViewModels/SettingsPanelViewModel.cs).
  - Acceptance: toggling a tier suppresses its key for the current resetISO.
  - Verify: FakeSnapshotInjector tee-up.
- [ ] **P1 — R3-P1-05 Option A** — Correct CHANGELOG + README wording re: WAV-only audio.
  - Touches: [README.md](README.md), [CHANGELOG.md](CHANGELOG.md). Add v0.1.1 entry.
  - Acceptance: README + CHANGELOG say WAV-only; MP3/M4A noted as planned.
- [ ] **P1 — R3-P1-06** — First-run toast suppression flag.
  - Touches: [Settings.Widget](src/QuotaGlass.Widget/Services/SettingsStore.cs), [MainWindow.xaml.cs Loaded handler](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs#L46-L51).
  - Acceptance: balloon tip fires once across all subsequent launches.
- [ ] **P1 — R3-P1-04** — `.github/workflows/ci.yml` (push + PR build/test).
  - Touches: new file.
  - Acceptance: failing test branch shows a red check on its PR.

Tag `v0.1.1` once all P0 items land.

### v0.2.0 — polish + true differentiator (existing ROADMAP Phase 2)

- [ ] **P1 — R3-P1-01** — Single-instance Mutex.
- [ ] **P1 — R3-P1-02** — `--collect-diagnostics` flag.
- [ ] **P1 — R3-P1-03** — Pace alarm tier (U2 rule family).
- [ ] **P1 — R3-P1-08** — Tray "Check for updates" wiring to UpdateChecker.
- [ ] **P1 — Pass 1 F-N1** — Direct credential reading (`~/.claude/.credentials.json`, `~/.codex/auth.json`, `~/.hermes/auth.json` — see Pass 2 R2-P1-05).
- [ ] **P2 — Pass 2 R2-P2-01** — Working-day pace integration (Zrnik pattern).
- [ ] **P2 — R3-P2-02** — Widget-side history ring buffer for sparklines.
- [ ] **P2 — ROADMAP NX-04** — Edge-snap on drag.
- [ ] **P2 — ROADMAP NX-06** — Catppuccin Latte light theme.
- [ ] **P2 — ROADMAP NX-08** — Sparkline panel (uses R3-P2-02).
- [ ] **P2 — ROADMAP NX-09** — Hover tooltip.
- [ ] **P2 — ROADMAP NX-10** — Embedded log panel.
- [ ] **P2 — R3-P1-05 Option B** — NAudio for MP3/M4A.
- [ ] **P2 — R3-P1-07** — Setup card dismiss-for-24h.
- [ ] **P2 — R3-P2-05** — DPI-safe ring center text.
- [ ] **P2 — R3-P2-06** — Per-bucket mute/snooze.
- [ ] **P2 — R3-P2-07** — Tray "Reset position" entry.
- [ ] **P3 — ROADMAP N-20** — Manual screenshots for `assets/screenshots/` (still open).

### v0.3+

- [ ] **R3-P2-01** — Multi-account columns within a provider.
- [ ] **R3-P2-03** — Schema v2 bundle history.
- [ ] **R3-P2-04** — Focus Assist awareness.
- [ ] **R3-P3-01..04** — Localization, Win11 Widgets board, plugin contract, Avalonia.

---

## 14. Pass 3 Verifications Performed

- **Read every shipped `.cs` and `.xaml` file** under `src/` against tag `v0.1.0` head (`100165e`).
- **Verified Pass 2 §3.7 sparkline claim against the actual `docs/extension-integration.md`** schema — claim was wrong; documented in Bug 4.
- **Confirmed `Icon.FromHandle` ownership semantics** via Microsoft Learn docs; documented in Bug 2.
- **Confirmed Mica composition behavior** against DWM API docs; documented in Bug 3.
- **Traced the R1 ladder walk** by hand-simulating cold-start at `T-5min` against the actual code; documented in Bug 1.
- **Cross-checked `NMH.csproj` RuntimeIdentifiers vs release.yml matrix arch** — mismatch confirmed in R3-P0-04.
- **Re-read all 11 xUnit tests** — coverage gap on AlarmScheduler/TrayIconService noted.
- **Cross-checked README/CHANGELOG language** vs shipped picker filter — MP3/M4A mismatch confirmed in R3-P1-05.
- **Could NOT verify:** arm64 runtime behavior (no device), current Codex analytics URL (no Codex Plus account in lab), HICON leak rate at scale (no GDI handle telemetry instrumentation).

---

*End of Pass 3. Pass 1 + Pass 2 + Pass 3 together cover the v0.1.0 ship and the v0.1.1 / v0.2.0 queue. Implementing agent should:* (a) *land R3-P0-01..05 + R3-P1-04..06 as v0.1.1 within ~5 days,* (b) *open the v0.2.0 batches in the order in §13,* (c) *treat ROADMAP.md as the live tracker; refresh it after each batch.*
