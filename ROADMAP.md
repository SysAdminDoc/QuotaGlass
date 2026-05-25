# Roadmap

**Last updated:** 2026-05-24 · **Current shipped version:** none yet (pre-v0.1.0)

This roadmap is a working document, not a wishlist. Each Now / Next / Later item ties back to a finding in [docs/research.md](docs/research.md). Under-Consideration entries are tracked but not committed. Rejected entries record decisions so they don't get silently re-litigated.

---

## Shipped

> Nothing yet.

---

## Now — v0.1.0 — "Foundation: bridge + widget MVP"

The goal is end-to-end: extension → NMH → snapshot.json → widget renders → toast fires at reset. Polish lives in v0.2.

### Bridge

- [ ] **N-01 — Native messaging host (`QuotaGlass.NMH`)**. .NET 9 console exe. stdin/stdout 4-byte LE length-prefix framing per [Chrome's protocol](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging). Origin validation against the allowed extension ID. JSON decode into `BucketSnapshot`.
- [ ] **N-02 — Snapshot persistence**. Atomic write to `%LOCALAPPDATA%\QuotaGlass\snapshot.json` (write to `.tmp`, `File.Move(..., overwrite: true)`). One snapshot per refresh tick; overwrite-in-place, no append.
- [ ] **N-03 — `--register` / `--unregister` flags**. Writes the NMH JSON manifest to the install dir and a registry key at `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.sysadmindoc.quotaglass` (plus Edge + Firefox equivalents if their roots exist).
- [ ] **N-04 — Stderr logging**. NMH cannot write to stdout (the channel is the protocol). All diagnostics to stderr + `%LOCALAPPDATA%\QuotaGlass\logs\nmh-{date}.log`.
- [ ] **N-05 — Extension bridge module**. Add `src/lib/bridge.js` to `AI-Usage_Tracker`, add `"nativeMessaging"` permission to `manifests/chrome.json` + `firefox.json`. On each successful refresh, forward latest snapshot. Userscript bundle skips this module entirely.

### Widget

- [ ] **N-06 — WPF window shell (`QuotaGlass.Widget`)**. Borderless `Window`, `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`. Draggable via `MouseLeftButtonDown` → `DragMove()`. Position persisted to `settings.json`.
- [ ] **N-07 — Catppuccin Mocha theme**. ResourceDictionary with the 26 named colors. All backdrops use 4–12 px `CornerRadius`. No `Capsule`-style shapes anywhere.
- [ ] **N-08 — Radial-ring countdown control**. Custom UserControl: `Path` with `ArcSegment`, sweep angle bound to `Percent`. Color ramp green (< 60 %) → amber (60–85 %) → red (> 85 %). HH:MM:SS center text bound to `TimeUntilReset`.
- [ ] **N-09 — Per-provider card layout**. One card per `BucketSnapshot`. Header = provider + label ("Claude — 5 h window", "Codex — weekly"), body = ring, footer = "Resets in 4 h 12 m".
- [ ] **N-10 — FileSystemWatcher on snapshot.json**. Debounced 250 ms; on change → reload + raise `INotifyPropertyChanged` on the bucket VM.
- [ ] **N-11 — Empty / stale / error states**. First-run: "Waiting for first snapshot from extension…" + install-link. Stale: ring greyed, "Last fetched 47 m ago". Error: red header bar, NMH connection state.

### Notifications

- [ ] **N-12 — Toast notification adapter**. Wrap `Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder`. Test with default Windows sound first; custom-audio in v0.2.
- [ ] **N-13 — Alarm-ladder scheduler**. `System.Threading.Timer` per tier per bucket. Tiers configurable: default 24 h / 12 h / 6 h / 3 h / 1 h / 30 m / 15 m / 5 m / at-reset. Fire-once idempotency keys: `<provider>-<bucket>-<tier>-<resetISO>`.
- [ ] **N-14 — Zero-state toast**. Special "R3" tier — fires when `percent >= 100` for any bucket, suppressed if `R3-<bucket>-<resetISO>` already fired. Sound: distinct from regular tiers.

### Settings

- [ ] **N-15 — Embedded settings panel**. Expand-down panel inside the widget (NOT a separate window). Refresh interval, alarm ladder toggles, theme, custom sound picker (deferred audio playback until v0.2).
- [ ] **N-16 — Settings persistence**. `%LOCALAPPDATA%\QuotaGlass\settings.json` with same atomic-write pattern.

### Distribution

- [ ] **N-17 — Inno Setup installer**. Single `QuotaGlass-Setup-vX.Y.Z.exe` that installs to `%LOCALAPPDATA%\Programs\QuotaGlass\`, runs `QuotaGlass.NMH.exe --register`, drops Start Menu shortcut, autostarts widget on login (optional checkbox).
- [ ] **N-18 — GitHub Release workflow**. `.github/workflows/release.yml`, `workflow_dispatch` only, builds + signs + uploads `Setup.exe` to the matching `vX.Y.Z` tag.

### Docs

- [ ] **N-19 — README "Install" section**. Real install steps, not placeholder.
- [ ] **N-20 — README screenshots**. Widget on dark wallpaper, toast in action, settings panel. DPI-aware capture per global screenshots rule.
- [ ] **N-21 — `docs/extension-integration.md`**. Specifies the snapshot payload schema the extension sends; canonical for future provider plugins.

---

## Next — v0.2.0 — "Polish + custom audio + tray"

Adds the features that take the widget from "MVP that works" to "actually pleasant to use all day."

- [ ] **NX-01 — Custom-sound picker fully wired**. OpenFileDialog → copy to `%LOCALAPPDATA%\QuotaGlass\sounds\<sha1>.<ext>` → reference via `file:///` in toast XML. Per-tier sound override.
- [ ] **NX-02 — System tray icon**. `NotifyIcon` (WinForms interop), right-click menu: Show / Hide widget, Refresh, Snooze 1 h, Open settings, Quit.
- [ ] **NX-03 — Snooze**. Suppress all toasts for a user-picked window. Snooze state persisted to `settings.json` so it survives a restart.
- [ ] **NX-04 — Edge-snap on drag**. Snap to top/bottom/left/right of the active monitor when dragged within 16 px. Catppuccin shadow drops to indicate snap zones.
- [ ] **NX-05 — Multi-monitor placement memory**. Per-monitor position state so DisplayPort renegotiation doesn't put the widget off-screen.
- [ ] **NX-06 — Catppuccin Latte light variant**. Theme switcher in settings + `prefers-color-scheme`-aware default.
- [ ] **NX-07 — Reduced-motion mode**. Respect `SystemParameters.MinimumAnimationFps` and the "Reduce motion" accessibility setting. Disables ring transitions + shimmer.
- [ ] **NX-08 — Sparkline panel**. 24-h mini history under each ring. Data from `snapshot.json` history array (extension already maintains 30 days).
- [ ] **NX-09 — Tooltip on ring hover**. Exact percent + timestamp + "X messages remaining" if extension provides the count.
- [ ] **NX-10 — Embedded log panel**. Collapse-down panel under settings; tail of `logs/widget-{date}.log` + NMH connection state.

---

## Later — v0.3+ — "Resilience + alarm power"

- [ ] **L-01 — Per-tier alarm sound + per-tier message**. Each ladder tier independently configurable: sound file, toast title template, action buttons.
- [ ] **L-02 — Calendar-style "next 7 days" view**. Expand widget into a card showing every upcoming Claude weekly + Codex 5 h reset over the next week.
- [ ] **L-03 — Win11 widget board integration**. Investigate the Windows 11 Widgets API (currently RSS/Web-Widget-API based, not great for custom .NET surfaces). May not be feasible without a UWP wrapper.
- [ ] **L-04 — Action Center deep-link**. Toast action buttons that open `claude.ai/settings/usage` or `chatgpt.com/codex/cloud/settings/analytics#usage`.
- [ ] **L-05 — Direct API fallback (Anthropic Admin / OpenAI Usage)**. User pastes an API key in settings; NMH polls those endpoints in addition to consuming the extension feed, so the widget keeps refreshing when Chrome is closed.
- [ ] **L-06 — Named pipe between NMH and Widget**. Currently they share via `snapshot.json` + FileSystemWatcher (~250 ms latency). Pipe drops it to < 10 ms and removes the disk roundtrip.
- [ ] **L-07 — Plan auto-detection (Maciek pattern)**. Detect Pro / Max5 / Max20 / Team / Enterprise from observed reset cadences. Surface as a badge on the card header.
- [ ] **L-08 — Burn-rate pace marker on ring**. Lighter tick on the ring showing projected end-of-window utilization (linear extrapolation from extension's existing burn-rate forecast).
- [ ] **L-09 — Anomaly / spike detection**. Toast when a single sample jumps > 2σ above moving average — the "single prompt jumped me from 21 % to 100 %" pain pattern.
- [ ] **L-10 — Provider plugin contract**. Each provider becomes a snapshot-source plugin (extension-fed, API-key-fed, JSONL-file-fed). Opens the door to NX-01..NX-06 in the extension's roadmap.

---

## Under Consideration — signals tracked, not committed

- **UC-01 — Avalonia port for Linux + macOS**. Requested by exactly nobody so far. The NMH is already portable; only the widget would need a port. Revisit if there's actual demand.
- **UC-02 — WinUI 3 / .NET MAUI port**. WinUI 3 gives Mica/Acrylic backdrops natively but the framework is still rough on extension/embedding scenarios. WPF + custom theme is more predictable in 2026.
- **UC-03 — Embed a browser cookie reader as fallback when NMH disconnects**. Only worth doing if direct-API (L-05) doesn't fully cover the disconnected-browser case.
- **UC-04 — Telemetry opt-in (Sentry / OpenTelemetry)**. README promises no telemetry. Adding even opt-in changes the privacy story. Park.
- **UC-05 — Companion mobile app (Android Material 3)**. Out of scope; would need a server to push extension snapshots to the phone, breaking the "nothing leaves your machine" promise.

---

## Rejected — decisions captured

- **R-01 — Rainmeter skin instead of standalone WPF**. Skin DSL is awkward for stateful logic; would still need a C# plugin for the alarm scheduler. Net cost is higher than WPF.
- **R-02 — Tauri or Electron desktop port of the extension**. AI-Usage_Tracker roadmap UC-01 already parks this as "browser-first by philosophy." Duplicates the JS data layer in a second runtime. ~150 MB install for a 32 px badge.
- **R-03 — Direct Chromium cookie reads as the primary data source**. Chromium changed cookie encryption mid-2024; maintenance treadmill. Acceptable as a fallback only (L-05 / UC-03).
- **R-04 — Re-implementing the entire scraping stack in WPF**. Forces the user to log in twice (browser + widget). Embedded WebView2 chrome looks awful as a widget. Re-fights every Claude / OpenAI hydration race the extension already solved.
- **R-05 — Pill / oval / fully-rounded backdrop UI**. Hard-banned by the global project rule on stadium shapes. No exceptions.
- **R-06 — Paid tier**. Open-source, MIT, no freemium gate.
- **R-07 — Confetti on reset**. Conflicts with the project's restrained design language (same as AI-Usage_Tracker R-03).
- **R-08 — GPL / copyleft license**. MIT is fine; copyleft would complicate downstream packaging into installers.

---

## Themes covered

| Category | Coverage |
|---|---|
| UX | N-06..N-11, NX-04..NX-09, L-02, L-08 |
| Performance | L-06 (named pipe), N-02 (atomic write) |
| Reliability | N-04 (stderr log), N-11 (stale state), L-05 (API fallback) |
| Security | N-01 (origin validation), NX-01 (file-copy sandbox) |
| Integrations | N-05 (extension bridge), L-04 (deep-links), L-05 (direct API) |
| Accessibility | NX-07 (reduced motion), L-07 implicit (plan auto-detect = no manual entry) |
| i18n / l10n | Out of v0.1; revisit in L-tier |
| Observability | N-04 (stderr + log), NX-10 (embedded log panel) |
| Distribution | N-17 (Inno installer), N-18 (release workflow) |
| Multi-monitor | NX-05 |
| Theme support | N-07, NX-06 |
