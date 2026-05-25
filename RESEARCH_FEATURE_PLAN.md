# Project Research and Feature Plan

**Project:** QuotaGlass · `~/repos/QuotaGlass/` · v0.1.0-dev (commit `b9061b7`, 2026-05-24).
**Repo:** PUBLIC, `SysAdminDoc/QuotaGlass`, MIT.
**Author of this report:** research pass after the v0.1.0-dev scaffold, before any v0.1.0 feature work begins.

This is a research-and-amendment document. It supplements [ROADMAP.md](ROADMAP.md) — it does NOT replace it. Roadmap items are referenced by their existing IDs (`N-01`..`N-22`, `NX-01`..`NX-10`, `L-01`..`L-10`); new items discovered during this pass are prefixed `R-` (Research) and should be merged into ROADMAP.md by the implementing agent.

---

## Executive Summary

QuotaGlass is a fresh-scaffold Windows desktop widget (.NET 9 WPF + console NMH) that displays Claude + Codex usage by piping snapshots over Chrome native messaging from the existing `AI-Usage_Tracker` browser extension. The scaffold builds clean and is structurally sound, but **end-to-end integration is not yet possible** because (a) the QuotaGlass `BucketSnapshot` model does not match the extension's actual emitted bucket shape, (b) the extension manifest has no `"key"` field so the Chrome extension ID is non-deterministic and the NMH's `allowed_origins` cannot point at it, (c) the Firefox extension ID hardcoded in `HostRegistrar.cs` is `aiusagetracker@sysadmindoc` but the real ID is `ai-usage-tracker@sysadmindoc.dev`, and (d) the proposed `bridge.js` disconnect-after-post pattern will trigger Chrome's MV3 service-worker idle termination, breaking continuous updates. **All four are blocking.** Beyond these, the strongest direction is to add a second data source — local OAuth credential reading from `~/.claude/.credentials.json` and `~/.codex/auth.json` — because 5+ Windows-native competitors already do this and the original positioning claim ("no Windows-native widget exists") was incorrect.

**Top 10 opportunities in priority order:**

1. **P0 — Match the extension's actual bucket schema** (Verified mismatch; see §3.1). Rewrite `BucketSnapshot.cs` to mirror what `defaultState()` in `AI-Usage_Tracker/src/lib/storage.js` actually emits.
2. **P0 — Pin the extension's Chrome ID** via a `"key"` field in `manifests/chrome.json`, then hardcode that ID in `HostRegistrar.cs`. Without this, native messaging cannot connect.
3. **P0 — Fix Firefox extension ID typo** in `HostRegistrar.FirefoxExtensionIds` to match the gecko id in `manifests/firefox.json`.
4. **P0 — Persistent port + reconnect bridge**, not fire-and-forget. Plain `port.postMessage(...).disconnect()` triggers Chrome MV3's 30s idle timer and the port dies.
5. **P0 — Verify custom-audio path on Windows 11 26100** with the legacy `Microsoft.Toolkit.Uwp.Notifications` package before committing the alarm UX to it. The newer `AppNotificationBuilder` API explicitly **disallows** `file:///` audio sources; the legacy XML path may or may not still work on current builds.
6. **P0 — Close-to-tray, not close-to-quit.** Today the × button kills the process. First-run users will lose the widget and not know how to bring it back.
7. **P1 — Add direct-credential data source** (`~/.claude/.credentials.json`, `~/.codex/auth.json`). Promotes roadmap `L-05` to v0.2 because every Windows competitor surveyed already does this and "browser must be open" is a positioning weakness.
8. **P1 — Replace planned Inno Setup with Velopack.** Free auto-update + delta packages + signed install, ~10 LOC integration. Inno Setup gives QuotaGlass an installer but no update mechanism, forcing every user to manually re-download for every patch.
9. **P1 — First-run + empty-state UX.** Today the widget shows "Waiting for first snapshot from extension…" forever if the user hasn't installed the extension or hasn't run `--register`. Add an in-widget "Setup checklist" with three actionable steps.
10. **P1 — Unit test the testable seams** (`AtomicJsonFile`, `MessagePump` framing, `BucketViewModel` countdown, `RadialRing` percent→sweep math). Zero tests exist today; these four cover the highest-risk surfaces and run in a vanilla `dotnet test` project.

---

## Evidence Reviewed

### Local files and directories inspected

- `~/repos/QuotaGlass/` — all 30 source / config / doc files in the repo (full enumerated list at top of git log).
- `~/repos/AI-Usage_Tracker/` — the upstream extension. Verified:
  - `manifests/chrome.json` (lines 1–99): permissions, content scripts, missing `"key"` field, missing `"nativeMessaging"` permission.
  - `manifests/firefox.json` (lines 10–15): `gecko.id = "ai-usage-tracker@sysadmindoc.dev"`, `strict_min_version: "115.0"`.
  - `src/lib/storage.js` (lines 84–120): `defaultState()` and `defaultSettings()` shapes — the canonical snapshot schema.
  - `src/lib/notify.js` (lines 1–166): rule evaluator (`R1-60`, `R1-15`, `R1-0`, `R2`, `U1-75/90/95`, `U2`, `D1`), fire-once key pattern, `humanReset()` formatter.
  - `src/lib/browser.js` (lines 15–87): `notify()`, `schedule()`, `send()`, `onMessage()` cross-runtime adapters.
  - `src/background.js` (lines 22–239): service worker entry, `aut/refresh` / `aut/scraped` / `aut/claude-message-limit` / `aut/claude-rate-limit-headers` message routing, `mergeSnapshot` reconciliation.
  - `src/scrapers/claude.js` (lines 12–115, 200–260, 340–490): Claude API path, bucket emission shape, fallback DOM scrape paths.
  - `src/scrapers/codex.js` (lines 80–180, 220–270, 480–540): WHAM API path, primary/secondary window normalization, per-model bucket expansion.
  - `src/ui/widget.css` (lines 1–80): existing in-page widget styling (Catppuccin tokens, corner radius, glass border conventions).
  - `dist/` directory listing — confirms versions v0.1.0 through v0.1.6 shipped.
  - `.github/workflows/` directory exists (contents not enumerated this pass).
- `~/repos/QuotaGlass/docs/research.md` — full research dossier written during scaffold; checked claims against actual code below.

### Git history reviewed

- `git log --stat --pretty=fuller` over the full QuotaGlass history (one commit: `b9061b7 v0.1.0-dev: initial scaffold`).
- `git log --oneline -20` on AI-Usage_Tracker for upstream context (covered by existing per-repo CLAUDE.md and ROADMAP.md in that repo).

### Build / test / docs / release artifacts inspected

- `dotnet build QuotaGlass.sln -c Release` → 0 warnings, 0 errors. Verified live.
- `dotnet ./src/QuotaGlass.NMH/bin/Release/net9.0-windows/QuotaGlass.NMH.dll --version` → prints `QuotaGlass.NMH 0.1.0.0`.
- `dotnet ./src/QuotaGlass.NMH/bin/Release/net9.0-windows/QuotaGlass.NMH.dll --help` → prints help. Verified live.
- No `.github/workflows/` directory in QuotaGlass yet (roadmap `N-18` not implemented).
- No `test/` or `tests/` directory in QuotaGlass yet (no unit test project exists).
- No `assets/screenshots/` populated (roadmap `N-20` not done; folder empty).
- No `installer/` artifacts populated (roadmap `N-17` not done; folder empty).

### External sources reviewed

- [Chrome for Developers — Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging) — verified registry path and `allowed_origins` wildcard rules.
- [Chrome — Extension service worker lifecycle](https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle) — confirmed 30s idle / 5min hard cap.
- [GoogleChrome/developer.chrome.com issue #2688](https://github.com/GoogleChrome/developer.chrome.com/issues/2688) — `connectNative` keep-alive failure reports.
- [anthropics/claude-code#16350](https://github.com/anthropics/claude-code/issues/16350) — 2026-01 incident where exactly this lifecycle issue broke a production native host.
- [Microsoft Learn — Send a local app notification from a C# app](https://learn.microsoft.com/en-us/windows/apps/design/shell/tiles-and-notifications/send-local-toast) — confirms `Microsoft.Toolkit.Uwp.Notifications` 7.1.3 still supported for unpackaged WPF as of late 2025.
- [Microsoft Learn — App notifications from UWP to WinUI migration](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/guides/toast-notifications) — Toolkit is archived; `AppNotificationBuilder` is the supported successor.
- [Microsoft Learn — Custom audio on app notifications](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/custom-audio-on-toasts) — **`ms-appdata` and `file:///` are explicitly UNSUPPORTED** in the new API; only `ms-appx:///` and `ms-resource`.
- [Velopack](https://github.com/velopack/velopack) and [docs.velopack.io](https://docs.velopack.io/) — modern Squirrel successor; cross-platform; auto-update + delta packages out of the box.
- [Zrnik/claude-usage-windows-taskbar-widget](https://github.com/Zrnik/claude-usage-windows-taskbar-widget) — WPF .NET 8, multi-account, reads `~/.claude/.credentials.json`, 14-day history JSON in `%APPDATA%`. Last release v0.2.20 on 2026-05-12.
- [CodeZeno/Claude-Code-Usage-Monitor](https://github.com/CodeZeno/Claude-Code-Usage-Monitor) — Rust taskbar widget, WSL credential support, tray badges.
- [jens-duttke/usage-monitor-for-claude](https://github.com/jens-duttke/usage-monitor-for-claude) — Python (pystray + pywebview), 12.5 MB portable EXE, **already supports custom shell-command webhooks and configurable threshold alerts**. v1.15.1 (2026-05-17).
- [psinghmanager/g4-Claw-counter](https://github.com/psinghmanager/g4-Claw-counter), [SlavomirDurej/claude-usage-widget](https://github.com/SlavomirDurej/claude-usage-widget), [SmartAppsCo/claude-usage-widget](https://github.com/SmartAppsCo/claude-usage-widget) — additional Windows-native trackers (deep architecture review deferred; positioning surfaced via search summaries).

### Areas that could not be verified this pass

- **Custom-audio behavior on Windows 11 build 26100** with the legacy `Microsoft.Toolkit.Uwp.Notifications` 7.1.3 package. Microsoft docs disagree across the legacy vs new APIs. **Needs live validation** on the user's actual machine before alarm UX commits to `file:///` audio.
- **AI-Usage_Tracker's `.github/workflows/`** — directory exists; contents not enumerated. May already have a build workflow QuotaGlass should mirror.
- **The actual JSON shape of `~/.claude/.credentials.json` and `~/.codex/auth.json`** under current Claude Code / Codex CLI versions. Competitor docs imply OAuth refresh tokens but field names not confirmed. **Needs live validation** before the direct-credential data-source code is written.
- **psinghmanager / SlavomirDurej / SmartAppsCo widget architectures** — surfaced via search but not individually deep-fetched.

---

## Current Product Map

### Core workflows (intended; not yet runtime-tested end-to-end)

1. **User installs QuotaGlass** (placeholder; installer N-17 not built).
2. **User installs the AI-Usage_Tracker extension** (already shipping at v0.1.6; needs PR for `"nativeMessaging"` permission + bridge module).
3. **Widget renders snapshot.json** read from `%LOCALAPPDATA%\QuotaGlass\snapshot.json`.
4. **NMH receives snapshot** from extension over stdin, atomically writes snapshot.json.
5. **Widget's FileSystemWatcher reloads** on each snapshot write, updates radial rings.
6. **Alarm scheduler fires toasts** at ladder thresholds before each bucket's reset (N-12 / N-13 not yet built).

### Existing features (today, verified by code)

| Feature | Where | Status |
|---|---|---|
| `BucketSnapshot` / `Bucket` model | `src/QuotaGlass.Shared/BucketSnapshot.cs` | Built, but schema does NOT match extension (see §3.1). |
| Atomic JSON file write | `src/QuotaGlass.Shared/AtomicJsonFile.cs` | Built, untested. |
| Well-known paths under `%LOCALAPPDATA%\QuotaGlass\` | `src/QuotaGlass.Shared/AppPaths.cs` | Built, working. |
| Native messaging host stdin/stdout pump | `src/QuotaGlass.NMH/MessagePump.cs` | Built, untested with real extension. |
| HKCU registry install for Chrome / Edge / Chromium / Firefox NMH | `src/QuotaGlass.NMH/HostRegistrar.cs` | Built; Firefox ID is wrong (see §3.3). |
| NMH `--version` / `--help` / `--register` / `--unregister` CLI | `src/QuotaGlass.NMH/Program.cs` | First two verified live; last two untested. |
| NMH stderr + file logger | `src/QuotaGlass.NMH/Logger.cs` | Built; no rotation. |
| WPF borderless always-on-top draggable shell | `src/QuotaGlass.Widget/Views/MainWindow.xaml` | Built, untested (no launch screenshot). |
| Catppuccin Mocha theme + control styles | `src/QuotaGlass.Widget/Theme/{CatppuccinMocha,Controls}.xaml` | Built. |
| Custom `RadialRing` (Path/ArcSegment percent ring) | `src/QuotaGlass.Widget/Controls/RadialRing.cs` | Built; no visual smoke test. |
| `SnapshotWatcher` (debounced FileSystemWatcher) | `src/QuotaGlass.Widget/Services/SnapshotWatcher.cs` | Built. |
| `BucketViewModel` (per-second countdown ticker) | `src/QuotaGlass.Widget/ViewModels/BucketViewModel.cs` | Built. |
| `MainViewModel` (snapshot → buckets reconciler) | `src/QuotaGlass.Widget/ViewModels/MainViewModel.cs` | Built; reconciles by `provider/label` not `id` (bug — see §3.2). |
| Minimize + close buttons in title bar | `MainWindow.xaml` lines 84–87 + `.xaml.cs` lines 27–35 | Built; "close" actually quits the process (bug — see §3.4). |

### Not-yet-built (advertised in README or ROADMAP)

| Feature | Roadmap ID | Status |
|---|---|---|
| Extension bridge module (`src/lib/bridge.js` in AI-Usage_Tracker) | N-05 | Not started. Blocking end-to-end. |
| Toast notification adapter | N-12 | Not started. Package referenced but never used. |
| Alarm-ladder scheduler | N-13 | Not started. |
| Zero-state R3 toast | N-14 | Not started. |
| Settings panel | N-15 | Not started. `AppPaths.SettingsFile` defined but never read or written. |
| Settings persistence | N-16 | Not started. |
| Inno Setup installer | N-17 | Not started. (Consider Velopack instead — see §6.) |
| Release workflow | N-18 | Not started. `.github/` directory does not exist. |
| Install section in README | N-19 | Section exists but contains "v0.1.0 has not shipped yet" placeholder. |
| Screenshots | N-20 | Not started. `assets/screenshots/` empty. |
| `docs/extension-integration.md` | N-21 | Not started. Schema contract is currently scattered across `BucketSnapshot.cs` and `docs/research.md` §3. |

### User personas

- **Heavy claude.ai web user** (Pro / Max5 / Max20 / Team) — primary persona; uses chat in browser, never touches Claude Code CLI. *Only QuotaGlass+extension currently serves them; competitors all require Claude Code creds.*
- **ChatGPT.com Codex user** — secondary; same browser-first profile. Same gap.
- **Mixed user** (browser + Claude Code CLI) — wants ONE widget that sees both surfaces. *Today QuotaGlass only sees browser side; competitors see only CLI side.*
- **Engineering lead watching their team's burn** — likely a v0.4+ persona; out of scope for v0.1–0.3.

### Platforms and distribution

- **Windows 10 build 1809+ / Windows 11** — explicit target via `SupportedOSPlatformVersion=10.0.17763.0` in `QuotaGlass.Widget.csproj`.
- **x64 only** (`RuntimeIdentifiers=win-x64`). ARM64 not currently a target — should add for v0.2 (cheap; one extra `dotnet publish -r win-arm64`).
- **Per-user install** (HKCU only). No HKLM / system-wide path.
- **GitHub Releases** as primary distribution (planned; not yet shipping).

### Integrations, permissions, storage, data flows

- **Browser extension** AI-Usage_Tracker → **stdin/stdout** to NMH → **`%LOCALAPPDATA%\QuotaGlass\snapshot.json`** → **FileSystemWatcher** in Widget. One-way data flow.
- **HKCU registry** writes by NMH `--register`: 4 subkeys under `Software\{Google\Chrome, Microsoft\Edge, Chromium, Mozilla}\NativeMessagingHosts\com.sysadmindoc.quotaglass`.
- **No outbound network** in QuotaGlass itself. Privacy story matches AI-Usage_Tracker's "nothing leaves your machine."

---

## Feature Inventory

### F-01 — Native messaging host

- **User value:** Connects the existing browser extension's authenticated data path to the desktop without re-implementing auth.
- **Entry point:** Browser extension calls `chrome.runtime.connectNative("com.sysadmindoc.quotaglass")`.
- **Main code:** `src/QuotaGlass.NMH/Program.cs`, `MessagePump.cs`, `HostRegistrar.cs`.
- **Maturity:** Partial. CLI flags verified live. Stdin/stdout framing untested end-to-end. **Blocking issues:** schema mismatch (§3.1), Firefox ID typo (§3.3), extension `"key"` not pinned (§3.5).
- **Tests/docs:** None.
- **Improvement opportunities:** R-01 through R-08 (see §10).

### F-02 — Snapshot persistence + atomic write

- **User value:** Widget keeps showing the last-known state when the browser is closed.
- **Entry point:** Internal — invoked by MessagePump on each inbound snapshot.
- **Main code:** `src/QuotaGlass.Shared/AtomicJsonFile.cs` (24 LOC), `AppPaths.cs`.
- **Maturity:** Built; works in principle but no test coverage. The `File.Replace` path is only exercised when the file already exists; the `File.Move` path on first write is correct but untested under concurrent reads from the Widget.
- **Tests/docs:** None.
- **Improvement opportunities:** R-09.

### F-03 — Always-on-top draggable glass widget

- **User value:** Persistent ambient display of current quota.
- **Entry point:** Launches the EXE.
- **Main code:** `src/QuotaGlass.Widget/Views/MainWindow.{xaml,xaml.cs}`, `Theme/CatppuccinMocha.xaml`, `Theme/Controls.xaml`.
- **Maturity:** Built, never visually tested. The "glass" effect is just `Opacity="0.92"` on a `SolidColorBrush` — not Mica or Acrylic. Looks translucent over the wallpaper but does NOT pick up Win11's system backdrop.
- **Tests/docs:** None. No screenshot in README.
- **Improvement opportunities:** R-10, R-11, R-12, R-13.

### F-04 — Radial-ring countdown control

- **User value:** Glanceable progress + time-to-reset.
- **Entry point:** Embedded in each bucket card.
- **Main code:** `src/QuotaGlass.Widget/Controls/RadialRing.cs` (151 LOC).
- **Maturity:** Built; rendering math is correct on paper but never visually verified. Color ramp green→amber→red at 60%/85% is more conservative than the extension's existing 75%/90% thresholds (`AI-Usage_Tracker/src/scrapers/claude.js` uses 75/90 in the popup CSS) — minor inconsistency.
- **Tests/docs:** None.
- **Improvement opportunities:** R-14, R-15.

### F-05 — Bucket view-model with countdown ticker

- **User value:** Live "Resets in 4h 12m" countdown that updates every second.
- **Entry point:** Bound to `BucketViewModel.TimeUntilResetLabel` in `MainWindow.xaml`.
- **Main code:** `src/QuotaGlass.Widget/ViewModels/BucketViewModel.cs` (lines 19–46).
- **Maturity:** Built. The 1-second `DispatcherTimer` ticks every visible bucket each second (`MainViewModel.cs` line 47–49). For 6 buckets this is 6 INPC events per second; cheap. Display correctness untested.
- **Tests/docs:** None.
- **Improvement opportunities:** R-16.

### F-06 — Snapshot watcher (FileSystemWatcher + debounce)

- **User value:** Widget updates near-instantly when NMH writes a new snapshot.
- **Entry point:** Internal — Widget startup.
- **Main code:** `src/QuotaGlass.Widget/Services/SnapshotWatcher.cs` (87 LOC).
- **Maturity:** Built. 250 ms debounce coalesces atomic-replace burst writes. Untested.
- **Tests/docs:** None.
- **Improvement opportunities:** R-17.

### F-07 — HKCU registrar (4 browsers)

- **User value:** One-shot `--register` installs all NMH manifests/registry keys.
- **Entry point:** `QuotaGlass.NMH.exe --register`.
- **Main code:** `src/QuotaGlass.NMH/HostRegistrar.cs` (lines 40–100).
- **Maturity:** Built. **Firefox ID hardcoded wrong** (`aiusagetracker@sysadmindoc` vs real `ai-usage-tracker@sysadmindoc.dev`). Chrome ID is a placeholder `aaaa…aaaa` and cannot be replaced with a real ID until the extension manifest pins it.
- **Tests/docs:** None.
- **Improvement opportunities:** R-18, R-19, R-20.

### F-08 — Stderr + file logger

- **User value:** Diagnostics for users debugging "why isn't the widget updating."
- **Entry point:** Internal — invoked by NMH on every event.
- **Main code:** `src/QuotaGlass.NMH/Logger.cs` (57 LOC).
- **Maturity:** Built. **No rotation, no size cap** — the log file grows monotonically. Path includes date (`nmh-2026-05-24.log`), so it's daily-bucketed, but old days never deleted.
- **Tests/docs:** None.
- **Improvement opportunities:** R-21.

---

## Competitive and Ecosystem Research

### C-01 — Zrnik/claude-usage-windows-taskbar-widget

- **Stack:** WPF, .NET 8 Desktop Runtime, MIT, last release 2026-05-12 v0.2.20.
- **Auth:** Reads `~/.claude/.credentials.json` (Claude Code OAuth) and `~/.codex/auth.json` (Codex CLI). Calls `api.anthropic.com/v1/messages` minimally to extract `anthropic-ratelimit-unified-5h-utilization` and `anthropic-ratelimit-unified-7d-utilization` headers.
- **Storage:** `%APPDATA%\ClaudeUsageWidget\history\` JSON, 14 days in 10-minute buckets.
- **Notable:** **Pins to taskbar adjacent to system tray**, multi-account columns side-by-side, auto-hides with taskbar / fullscreen apps, "Run at startup" right-click toggle, multi-monitor (bottom-right of every monitor), color ramp green<75% / orange 75–90% / red ≥90%.
- **Learn:** (a) The taskbar-pinned positioning is a strong UX pattern — less intrusive than always-on-top floating. (b) Multi-account columns are a power-user feature competitors converge on. (c) Their color thresholds (75/90) are likely tuned from real usage — adopt them.
- **Intentionally avoid:** (a) No notifications. (b) No settings UI. (c) Auto-hide tied to taskbar visibility is too clever — fullscreen-game users would want the widget back when they Alt-Tab out.

### C-02 — CodeZeno/Claude-Code-Usage-Monitor

- **Stack:** Rust, MIT.
- **Auth:** Same credential reading pattern as C-01, plus **WSL distro enumeration** — reads creds from inside installed WSL distros.
- **Notable:** **Tray-icon badges with usage percent** (no floating window at all), separate icons per model, falls back to rate-limit headers if newer endpoints fail.
- **Learn:** WSL credential support is a real differentiator for Linux-in-Windows users. Defer for QuotaGlass v0.2+.
- **Intentionally avoid:** Tray-icon-only with no floating surface loses the ambient "I can see it on my desktop" affordance.

### C-03 — jens-duttke/usage-monitor-for-claude

- **Stack:** Python + pystray + pywebview, single 12.5 MB portable EXE, MIT, v1.15.1 2026-05-17.
- **Auth:** Reads `~/.claude/.credentials.json` for OAuth tokens; never accepts API key.
- **Notable:** **Configurable threshold alerts per quota type**, **"time-aware mode"**, **reset notifications**, **custom shell-command webhooks for sounds / external integrations**, "automatic token refresh" via background `claude update`, modular design with security-critical code isolated.
- **Learn:** **The custom-shell-command webhook pattern is brilliant.** Lets power users wire QuotaGlass alarms into ntfy, Discord, Home Assistant, etc., without QuotaGlass needing to maintain integrations. Add as v0.3 feature.
- **Intentionally avoid:** Python runtime in the EXE is ~12 MB. WPF + .NET 9 framework-dependent publish is ~600 KB widget + ~12 KB NMH. We're already smaller.

### C-04 — psinghmanager/g4-Claw-counter

- **Notable:** **Multi-provider** (Claude Code, Claude Desktop, OpenAI Codex, more), **user-editable pricing tables**, "today's cost" display, "zero AI tokens consumed" (reads only local files).
- **Learn:** User-editable pricing is a low-effort feature — ship a JSON file, let users override. Defer to v0.4 cost-tracking work (matches AI-Usage_Tracker roadmap NX-12).

### C-05 — Tokens 4 Breakfast (macOS, paid)

- Already covered in `docs/research.md` §5. Confirmed: macOS menu-bar paid app, not a Windows competitor. Inspiration source for Focus Mode / month-end bill prediction (defer to v0.4+).

### C-06 — hamed-elfayome/Claude-Usage-Tracker (macOS)

- Already covered in `docs/research.md`. Confirmed: macOS only.

### C-07 — ryoppippi/ccusage (CLI)

- Already covered. Complementary, not competitive. Reads `~/.claude/projects/*.jsonl` for per-session-block analytics.

### Repositioning conclusion

**The original positioning claim in `docs/research.md` §5 ("There is no Windows-native floating widget for Claude + Codex usage as of May 2026") is incorrect and must be revised.** At least six Windows-native widgets exist (C-01..C-04 + SlavomirDurej/claude-usage-widget + SmartAppsCo/claude-usage-widget). All read Claude Code CLI credentials; **QuotaGlass's actual differentiator is being the only one that tracks browser sessions** (claude.ai chat web users, chatgpt.com Codex web users) **plus** the alarm-ladder + custom-audio surface.

Strategic implication: **QuotaGlass should add direct credential reading as a SECOND data source** so it covers both the web-user and the Claude Code/Codex CLI user — this lifts QuotaGlass from "the browser-only one" to "the only one that covers both surfaces with one widget."

---

## Highest-Value New Features

### F-N1 — Direct credential reading as fallback / supplement data source

- **User problem solved:** Widget shows nothing when the browser is closed. Mixed CLI+web users have to install two trackers.
- **Evidence:** 5+ Windows competitors (C-01..C-04) already do this; positioning §C confirms this is table stakes. Roadmap `L-05` is the same feature, currently parked in "Later."
- **Proposed behavior:** New NMH mode `--poll-credentials` runs an internal timer (configurable, default 5 min) that reads `%USERPROFILE%\.claude\.credentials.json` and `%USERPROFILE%\.codex\auth.json`, makes a minimal `api.anthropic.com/v1/messages` call to extract rate-limit headers (Zrnik's exact pattern), and writes snapshot.json. Runs alongside the extension bridge; whichever path produces a fresher reading wins per-bucket.
- **Implementation areas:** New `QuotaGlass.NMH.CredentialPoller` class. New `QuotaGlass.Shared.ClaudeCodeCredentials` reader. NMH gains a settings file to control poll cadence + enable flag. NSSM or `Task Scheduler` registration to keep the NMH alive even without an open browser (separate from the per-extension-spawn lifecycle).
- **Data model implications:** None at the schema level — same `BucketSnapshot`. New `SnapshotSource.Headers` already in the enum; add `SnapshotSource.LocalCreds`.
- **Risks:** (a) Claude Code's credential file format may change without warning; need version detection + graceful fail-back to extension-only. (b) Anthropic may throttle minimal /v1/messages calls used purely for headers. (c) Running NMH as a long-lived process changes the "spawned per connection" lifecycle assumption.
- **Verification plan:** Smoke test: rename `.credentials.json`, watch NMH log; restore, watch snapshot.json update. Manually verify with extension uninstalled.
- **Estimated complexity:** L.
- **Priority:** P1 (becomes table stakes given competitive landscape).

### F-N2 — Auto-update via Velopack

- **User problem solved:** Every QuotaGlass patch otherwise requires the user to re-download the installer manually.
- **Evidence:** Every Windows competitor surveyed has a release cadence — Zrnik shipped v0.2.20 just 12 days before our v0.1.0; jens-duttke shipped v1.15.1 a week ago. Users will not chase manual updates. Velopack is the modern Squirrel successor (lineage: Squirrel.Windows → Clowd.Squirrel → Velopack), ~10 LOC to integrate, free, MIT.
- **Proposed behavior:** Replace planned Inno Setup (roadmap `N-17`) with Velopack. Velopack ships installer + auto-update + delta packages + signed publishing. On launch, widget calls `UpdateManager.CheckForUpdatesAsync()`; on update found, downloads delta and offers a "Restart to update" toast.
- **Implementation areas:** New `installer/` content. Add `Velopack` package reference. Add `VelopackApp.Build().Run()` call in `App.xaml.cs` `OnStartup` (before WPF init). Generate releases via `vpk pack` in the release workflow.
- **Data model implications:** None.
- **Risks:** Velopack updates require app restart (acceptable for a widget). Code signing certificate desirable but not required (Velopack works unsigned, just with the SmartScreen prompt that all unsigned installers get).
- **Verification plan:** Build v0.1.0, deploy locally; bump to v0.1.1, re-pack, point local `feed` URL at the v0.1.1 manifest; confirm widget detects update + applies it.
- **Estimated complexity:** M.
- **Priority:** P1 (replaces N-17).

### F-N3 — In-widget Setup Checklist for first-run

- **User problem solved:** Today's empty state ("Waiting for first snapshot from extension…") is a dead end. The user has no idea whether they need to install the extension, run `--register`, or restart Chrome.
- **Evidence:** No competitor has good first-run UX — this is a free positioning win. The README's planned "Install" section is also placeholder-only (`README.md` lines 89–100).
- **Proposed behavior:** When `snapshot.json` is missing OR older than 24 h, render a Setup card with three checkbox-style steps:
  1. ✅/⏳ AI-Usage_Tracker extension installed — checked when NMH stderr log shows any incoming connection. Click "Install" → opens Chrome Web Store / Releases page.
  2. ✅/⏳ Native messaging registered — checked when `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.sysadmindoc.quotaglass` exists. Click "Register" → spawns `QuotaGlass.NMH.exe --register` and waits.
  3. ✅/⏳ First snapshot received — checked when `snapshot.json` exists. Click "Help" → opens the README troubleshooting section.
- **Implementation areas:** New `Views/SetupCard.xaml`. `MainViewModel` gains `Mode` enum (Setup / Live / Stale). New `Services/HealthCheck.cs` runs the three checks in the background.
- **Data model implications:** None.
- **Risks:** Registry read for step 2 requires the same per-user HKCU access we already write to — safe.
- **Verification plan:** Fresh machine, install QuotaGlass alone (no extension) → see Setup card with steps 1+2+3 unchecked. Install extension → step 1 checks. Run `--register` from the card → step 2 checks. Wait for next refresh → step 3 checks. Card collapses into normal Live view.
- **Estimated complexity:** M.
- **Priority:** P0 (gates v0.1.0 user acceptance).

### F-N4 — System tray icon with badge + minimize-to-tray default

- **User problem solved:** Today the × button in the title bar quits the process (`MainWindow.xaml.cs` line 33 `Close()` → `ShutdownMode="OnMainWindowClose"` in App.xaml line 5). First-run users will quit it and not know how to bring it back. The widget also has no presence when minimized.
- **Evidence:** Every Windows competitor surveyed has a tray icon (C-01 right-click + C-02 tray badges + C-03 pystray). Roadmap `NX-02` plans this for v0.2 — promote to v0.1.
- **Proposed behavior:** `NotifyIcon` (WinForms interop or `H.NotifyIcon.Wpf` package) with right-click menu: Show widget, Refresh now, Open settings, Quit. Title-bar × becomes Hide-to-tray. Double-click tray → toggle widget visibility. Tray icon overlays an aggregate worst-bucket % badge with the same green/amber/red ramp.
- **Implementation areas:** New `Services/TrayIconService.cs`. `App.xaml.cs` switch to `ShutdownMode="OnExplicitShutdown"`. `MainWindow.xaml.cs` `OnCloseClick` → `Hide()` instead of `Close()`. New PNG icons for the three tray states.
- **Data model implications:** None.
- **Risks:** `H.NotifyIcon.Wpf` is a maintained MIT package (vs the Forms NotifyIcon interop hassle). Both safe.
- **Verification plan:** Click ×, confirm widget hides but process keeps running (Task Manager shows `QuotaGlass.Widget.exe` still present). Right-click tray → Show widget → widget reappears. Right-click tray → Quit → process actually exits.
- **Estimated complexity:** M.
- **Priority:** P0 (gates v0.1.0).

### F-N5 — Mica / Acrylic backdrop for "real" Win11 glass

- **User problem solved:** Today's "glass" effect is just a translucent SolidColorBrush. On Win11 a real `DesktopAcrylicBackdrop` or `MicaBackdrop` looks far better — picks up wallpaper colors, has the system blur, behaves correctly when the desktop is locked.
- **Evidence:** Win11 introduced `Microsoft.UI.Composition.SystemBackdrops`. For WPF, the path is via `DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ...)` — Build 22621+. Fallback: keep the current SolidColorBrush on older Windows.
- **Proposed behavior:** On startup, query Windows build. If ≥ 22621, set system backdrop via P/Invoke; if < 22621, retain current behavior. Settings panel exposes "Backdrop: Auto / Mica / Acrylic / Solid".
- **Implementation areas:** New `Services/SystemBackdropService.cs`. `MainWindow.xaml.cs` `Loaded` handler queries + applies. Update `Brush.Window.Background` to be `Transparent` when backdrop is active.
- **Data model implications:** Settings file gains `theme.backdrop` field.
- **Risks:** Mica only renders on focused windows by default — for a widget that needs to remain visually present when unfocused, force `DWMWA_USE_HOSTBACKDROPBRUSH` (not all builds support it). Acrylic works regardless of focus.
- **Verification plan:** Run on Win11 23H2 → confirm Mica visible. Run on Win10 → confirm fallback. Drag widget across wallpapers; confirm color picks up.
- **Estimated complexity:** S.
- **Priority:** P2 (visual polish, not gating).

### F-N6 — "Open analytics" deep-link on ring click

- **User problem solved:** Today the only way to drill into a specific bucket is to manually open the provider's settings page.
- **Evidence:** Extension already has `aut/open-analytics` handler that opens `https://claude.ai/settings/usage` or the Codex analytics URL. Free re-use.
- **Proposed behavior:** Click a Claude card → opens `https://claude.ai/settings/usage` in default browser. Click Codex card → opens `https://chatgpt.com/codex/cloud/settings/analytics#usage`. Use `Process.Start` with the URL.
- **Implementation areas:** `MainWindow.xaml` ItemTemplate gains a `Border.MouseLeftButtonUp` event. `MainWindow.xaml.cs` adds `OpenAnalytics(Provider)`.
- **Data model implications:** None.
- **Risks:** Some users on non-default browsers may be confused if analytics opens in Edge but they use Brave. Mitigation: use `ShellExecuteEx` with the URL (respects default browser).
- **Verification plan:** Click each card, confirm correct URL opens in default browser.
- **Estimated complexity:** S.
- **Priority:** P1.

### F-N7 — Custom shell-command webhook on alarm fire

- **User problem solved:** Users with ntfy / Home Assistant / Discord / Slack setups want their alarm fires to trigger those flows.
- **Evidence:** jens-duttke (C-03) ships this feature; users love it. Roadmap `L-04` plans the same idea via direct integrations — shell-command is strictly more flexible (one feature serves any integration).
- **Proposed behavior:** Per-tier settings field "On fire: run command". Command runs with environment variables `QG_PROVIDER`, `QG_BUCKET_ID`, `QG_PERCENT`, `QG_RESET_ISO`, `QG_TIER`. 5-second timeout, stderr captured to widget log.
- **Implementation areas:** `Services/AlarmScheduler.cs` (when built) gains webhook hook. Settings panel gains command-textbox per tier.
- **Data model implications:** Settings file gains `alarms.<tier>.command` field.
- **Risks:** Command injection (mitigation: pass via env vars, never substitute into command string). Long-running commands (mitigation: 5 s hard kill).
- **Verification plan:** Set command to `cmd /c echo %QG_PROVIDER% %QG_PERCENT% >> %TEMP%\quotaglass-test.log`; trigger a synthetic alarm; confirm log line appears.
- **Estimated complexity:** S.
- **Priority:** P2 (delights power users).

### F-N8 — Snapshot-injection test mode for solo widget dev

- **User problem solved:** Today the widget cannot be developed without the full extension+NMH+browser chain running. Most widget work would benefit from running the WPF in isolation with synthetic data.
- **Evidence:** Standard pattern in widget shells. Pure dev-ergonomics.
- **Proposed behavior:** `QuotaGlass.Widget.exe --inject-fake-snapshot` writes a deterministic fake `snapshot.json` to `%LOCALAPPDATA%\QuotaGlass\` with Claude 5h 64% / weekly 87% and Codex 5h 23% / weekly 91%, with reset times spread across the day. Widget reads as normal.
- **Implementation areas:** `App.xaml.cs` adds args handling. New `Services/FakeSnapshotInjector.cs`.
- **Data model implications:** None.
- **Risks:** None — gated behind a CLI flag, not user-discoverable.
- **Verification plan:** `dotnet run --project src/QuotaGlass.Widget -- --inject-fake-snapshot` → widget renders four bucket cards with the canned values.
- **Estimated complexity:** S.
- **Priority:** P0 (gates anyone else being able to iterate on the widget).

### F-N9 — `docs/extension-integration.md` snapshot schema spec

- **User problem solved:** The contract between the extension and the NMH is currently scattered across `BucketSnapshot.cs`, `docs/research.md` §3, and reverse-engineered from `AI-Usage_Tracker/src/lib/storage.js`. A single canonical spec eliminates drift.
- **Evidence:** Roadmap `N-21` is this exact task.
- **Proposed behavior:** Single Markdown doc that pins: JSON envelope shape, every field's type + meaning, fixture JSON for v1 + the test buckets (Claude session, Claude weekly all, Claude weekly sonnet, Claude weekly design, Codex 5h all, Codex weekly all + per-model expansions), the fire-once key shape, the schema-version bump policy.
- **Implementation areas:** New `docs/extension-integration.md`. Referenced from `BucketSnapshot.cs` doc-comments. Referenced from `AI-Usage_Tracker/CLAUDE.md`.
- **Data model implications:** Forces us to actually decide whether to mirror the extension's shape exactly (recommended) or invent a normalized shape.
- **Risks:** None — pure docs.
- **Verification plan:** Inspectable; reviewable by humans.
- **Estimated complexity:** S.
- **Priority:** P0 (blocks F-A1 / F-A2 below).

### F-N10 — ARM64 build target

- **User problem solved:** Surface Pro X / Snapdragon-X laptops cannot run x64-only QuotaGlass natively (only via emulation, with a perf hit).
- **Evidence:** `QuotaGlass.NMH.csproj` and `QuotaGlass.Widget.csproj` both have `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`. .NET 9 supports `win-arm64` trivially.
- **Proposed behavior:** Add `win-arm64` to both csprojs. Release workflow builds both architectures, packages each as a separate installer.
- **Implementation areas:** Two csproj edits. Release workflow matrix.
- **Data model implications:** None.
- **Risks:** ARM64 toast / FileSystemWatcher behavior should be identical to x64; the runtime abstracts it.
- **Verification plan:** `dotnet publish -r win-arm64` builds clean. Confirm runs on an ARM64 Win11 device if available.
- **Estimated complexity:** S.
- **Priority:** P2.

---

## Existing Feature Improvements

(IDs `F-A*` for "Feature Amendment.")

### F-A1 — Rewrite `BucketSnapshot` to match the extension's actual shape

- **Current behavior:** `src/QuotaGlass.Shared/BucketSnapshot.cs` defines `Bucket { Provider, Label, Plan, Percent, ResetIso, Source, Model }` with no `Id` or `Kind` fields. The extension's actual emitted shape (verified in `AI-Usage_Tracker/src/lib/storage.js` lines 84–92 + `src/lib/notify.js` lines 27, 32, 47, 67, 89, 92) is:
  ```
  state.snapshot = {
    fetchedAtISO: string,
    providers: {
      claude: { ok, provider:'claude', source, orgId, plan, buckets: [Bucket] },
      codex:  { ok, provider:'codex',  source, accountId, plan, buckets: [Bucket] },
    }
  }
  Bucket = { id, kind:'session'|'5h'|'weekly', model, label, percentUsed, resetISO, rawResetText }
  ```
  Field-by-field mismatches:
  - **`Percent` vs `percentUsed`** — different JSON property names; deserialization will silently produce 0% on every bucket.
  - **No `Id` in our model** — but the extension's notification rule keys are built from `bucket.id` (`AI-Usage_Tracker/src/lib/notify.js` line 33). Without `Id` we can't reproduce the fire-once key pattern.
  - **No `Kind` in our model** — but rule evaluation depends on it (e.g. `bucket.kind === 'weekly'` at line 83 for burn-rate forecast).
  - **No `RawResetText`** — fallback for buckets where the API doesn't give a parseable ISO timestamp.
  - **No nested `ProviderSnapshot`** — our model is a flat `List<Bucket>` with `Provider` on each; theirs is a per-provider object with `ok`/`error`/`source`/`plan` metadata that we'd lose.
- **Problem:** End-to-end integration cannot work. The extension's payload will deserialize to a snapshot with all-zero percent values and no reset times.
- **Recommended change:** Replace `BucketSnapshot.cs` with a model that mirrors the extension shape exactly:
  ```csharp
  public sealed class StateEnvelope { int SchemaVersion; DateTimeOffset Ts; ProviderMap Snapshot; }
  public sealed class ProviderMap { ProviderSnapshot? Claude; ProviderSnapshot? Codex; }
  public sealed class ProviderSnapshot { bool Ok; string Provider; string Source; string? OrgId; string? Plan; List<Bucket> Buckets; string? Error; }
  public sealed class Bucket { string Id; string Kind; string Model; string Label; double PercentUsed; DateTimeOffset? ResetIso; string? RawResetText; }
  ```
  Use `[JsonPropertyName("percentUsed")]` and `[JsonPropertyName("resetISO")]` to match camelCase + the extension's `resetISO` capitalization.
- **Code locations:** `src/QuotaGlass.Shared/BucketSnapshot.cs` (full rewrite). `src/QuotaGlass.Widget/ViewModels/BucketViewModel.cs` (consume new shape). `src/QuotaGlass.Widget/ViewModels/MainViewModel.cs` (reconcile by `Bucket.Id` not `Provider/Label`). `src/QuotaGlass.NMH/MessagePump.cs` (decode new shape).
- **Backward compatibility:** None to preserve — nothing has shipped.
- **Verification plan:** F-N9 spec + a synthetic JSON fixture deserialized in a unit test asserts every field round-trips.
- **Estimated complexity:** M.
- **Priority:** **P0** (blocks every other feature past v0.1.0).

### F-A2 — Add `"key"` to the AI-Usage_Tracker manifest so the Chrome ID is pinned

- **Current behavior:** `AI-Usage_Tracker/manifests/chrome.json` has no `"key"` field. Chrome computes the extension ID from a hash of the install path (or the developer key if present). Different users get different IDs. The NMH's `allowed_origins` array requires exact IDs — no wildcards.
- **Problem:** Without a pinned `"key"`, every user's extension gets a different ID, so the NMH's hardcoded `allowed_origins` will reject every user except the one whose extension ID the placeholder happens to match. Chrome documentation confirms `allowed_origins` does not support wildcards.
- **Recommended change:** Generate an RSA 2048 keypair (`openssl genrsa -out aut-ext.pem 2048`). Compute the deterministic Chrome ID via the SHA-256 of the public key (Chrome's algorithm; documented). Add the base64-encoded public key as `"key"` in `manifests/chrome.json`. Hardcode the resulting ID in `QuotaGlass.NMH/HostRegistrar.cs`. Document the ID in `docs/extension-integration.md`.
- **Code locations:** `AI-Usage_Tracker/manifests/chrome.json` (add `"key"` field). `QuotaGlass.NMH/HostRegistrar.cs` (replace `aaaa…aaaa`).
- **Backward compatibility:** Existing extension installs will get a new ID once `"key"` is added (their previous local data may be lost unless they migrate). For an unshipped extension this is fine; for one with users, the migration is "uninstall + reinstall." Since AI-Usage_Tracker is at v0.1.6 and probably has very few users beyond the author, this is acceptable.
- **Verification plan:** Build extension, load unpacked, confirm ID matches the computed value. Install QuotaGlass NMH, run `--register`, open Chrome, watch NMH log for successful inbound connection.
- **Estimated complexity:** S.
- **Priority:** **P0**.

### F-A3 — Fix Firefox extension ID typo in HostRegistrar

- **Current behavior:** `HostRegistrar.cs` line 24 hardcodes `"aiusagetracker@sysadmindoc"`.
- **Problem:** The real ID per `AI-Usage_Tracker/manifests/firefox.json` line 12 is `"ai-usage-tracker@sysadmindoc.dev"`. Firefox will reject the connection.
- **Recommended change:** Update the array to `["ai-usage-tracker@sysadmindoc.dev"]`. Add a comment with a URL pointer to the source-of-truth manifest.
- **Code locations:** `src/QuotaGlass.NMH/HostRegistrar.cs` line 24.
- **Backward compatibility:** N/A (never shipped).
- **Verification plan:** Install in Firefox Developer Edition, run `--register`, open about:debugging → inspect AI-Usage_Tracker background script, attempt `browser.runtime.connectNative("com.sysadmindoc.quotaglass")`, watch NMH log.
- **Estimated complexity:** XS.
- **Priority:** **P0**.

### F-A4 — Persistent port + reconnect-on-disconnect in `bridge.js`

- **Current behavior:** `docs/research.md` §7 proposes:
  ```js
  const port = chrome.runtime.connectNative("com.sysadmindoc.quotaglass");
  port.postMessage({ kind: "snapshot", buckets: ..., ts: Date.now() });
  port.disconnect();
  ```
- **Problem:** `port.disconnect()` after each post defeats the purpose of `connectNative()`'s "strong keep-alive." Chrome's documentation [says](https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle) "connecting to a native messaging host using `chrome.runtime.connectNative()` will keep a service worker alive." Per [issue #2688](https://github.com/GoogleChrome/developer.chrome.com/issues/2688) and the 2026-01 [claude-code#16350](https://github.com/anthropics/claude-code/issues/16350) incident, this guarantee can also fail in practice — extensions need defensive reconnect.
- **Recommended change:** In `bridge.js`:
  ```js
  let port = null;
  function ensurePort() {
    if (port) return port;
    port = chrome.runtime.connectNative("com.sysadmindoc.quotaglass");
    port.onDisconnect.addListener(() => { port = null; /* reconnect on next post */ });
    port.onMessage.addListener((msg) => console.log("[QG] ack", msg));
    return port;
  }
  export function pushSnapshot(state) {
    try { ensurePort().postMessage({ kind: "snapshot", v: 1, ts: Date.now(), state }); }
    catch (e) { port = null; console.warn("[QG] push failed; will retry next tick", e); }
  }
  // periodic keepalive ping every 25s — under Chrome's 30s idle threshold
  setInterval(() => { try { ensurePort().postMessage({ kind: "ping" }); } catch {} }, 25_000);
  ```
- **Code locations:** New `AI-Usage_Tracker/src/lib/bridge.js`. Hook from `background.js` `refreshNow()` after `await saveState(next)` in `mergeSnapshot`. NMH needs to accept `kind: "ping"` and reply `{ ok: true, kind: "pong" }`.
- **Backward compatibility:** New module; no existing behavior to preserve.
- **Verification plan:** Open `chrome://serviceworker-internals`, find AI-Usage_Tracker, watch its "Running for" counter — should stay >5 min instead of cycling back to 0 every 30s.
- **Estimated complexity:** M.
- **Priority:** **P0**.

### F-A5 — Reconcile buckets by `Bucket.Id` not `Provider/Label`

- **Current behavior:** `MainViewModel.KeyOf(Bucket)` at line 81 returns `$"{b.Provider}/{b.Label}"`.
- **Problem:** Labels can change between extension versions (e.g. "5 hour usage limit" → "5-hour limit"). Bucket IDs are stable (`claude-session`, `claude-weekly-all`, `codex-5h-all`, etc. — see `scrapers/claude.js` lines 363/370 and `scrapers/codex.js` lines 106/120/142/156). Changing a label silently rebuilds the entire row, losing UI continuity.
- **Recommended change:** After F-A1 adds the `Id` field, change `MainViewModel.KeyOf` to return `b.Id`. Falls out naturally.
- **Code locations:** `src/QuotaGlass.Widget/ViewModels/MainViewModel.cs` lines 79–83.
- **Backward compatibility:** N/A.
- **Verification plan:** Unit test asserting that swapping a bucket's `Label` does NOT cause a remove+add in the `Buckets` ObservableCollection.
- **Estimated complexity:** XS.
- **Priority:** P0 (rolls in with F-A1).

### F-A6 — Validate custom-audio playback before committing to it

- **Current behavior:** `docs/research.md` §6 asserts that `file:///<absolute path>` is "the only reliable scheme" for custom toast audio in unpackaged WPF.
- **Problem:** Microsoft's [current docs](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/custom-audio-on-toasts) for the new `AppNotificationBuilder` API explicitly list `ms-appdata` and `file:///` as **unsupported**. Only `ms-appx:///` and `ms-resource` are supported in the new path. The legacy `Microsoft.Toolkit.Uwp.Notifications` XML path may or may not still accept `file:///` on Windows 11 build 26100 (the user's machine).
- **Recommended change:** Build a 20-line spike: `ToastContentBuilder.AddText("test").AddAudio(new Uri("file:///C:\\Users\\...\\test.wav")).Show()`. Verify whether audio plays on the user's actual 26100 build. If it works: document the path. If not: fall back to playing audio via `System.Media.SoundPlayer.Play()` from the alarm scheduler **alongside** the silent toast — toast for visual, SoundPlayer for audio.
- **Code locations:** Spike in a scratch test project. Findings update `docs/research.md` §6.
- **Backward compatibility:** N/A.
- **Verification plan:** Manual: spike + listen.
- **Estimated complexity:** S.
- **Priority:** **P0** (blocks the entire alarm UX direction).

### F-A7 — Title-bar `×` should hide, not quit

- **Current behavior:** `MainWindow.xaml.cs` `OnCloseClick` calls `Close()`; `App.xaml` has `ShutdownMode="OnMainWindowClose"`. The result: `×` exits the process and the user loses the widget with no way to bring it back short of re-launching the EXE.
- **Problem:** Violates user expectation for tray-resident widgets. Severe first-run footgun.
- **Recommended change:** Switch `ShutdownMode="OnExplicitShutdown"`. `OnCloseClick` → `Hide()`. Pair with F-N4 tray icon so the user can bring the widget back. Until F-N4 lands, also bind a global `Win+Shift+Q` hotkey via `RegisterHotKey` to toggle visibility.
- **Code locations:** `App.xaml` line 5. `MainWindow.xaml.cs` `OnCloseClick`. (Later F-N4 expands.)
- **Backward compatibility:** N/A.
- **Verification plan:** Click ×; confirm window hides but `tasklist /fi "imagename eq QuotaGlass.Widget.exe"` still shows the process.
- **Estimated complexity:** S.
- **Priority:** **P0**.

### F-A8 — Honor the extension's existing 75/90 thresholds in the ring color ramp

- **Current behavior:** `RadialRing.OnRender` (line 105) uses thresholds `>= 85` red / `>= 60` amber.
- **Problem:** The extension's notification rules (`AI-Usage_Tracker/src/lib/storage.js` line 110–112) and the visible widget CSS use 75 / 90 thresholds. Inconsistency between the desktop ring and the in-browser ring is jarring.
- **Recommended change:** Move thresholds to `BucketViewModel` (compute the `Brush` once per snapshot, not on every render). Default to 75 / 90. Settings panel exposes them.
- **Code locations:** `RadialRing.cs` lines 100–107. `BucketViewModel.cs`. Settings file gains `display.warnPercent` / `display.dangerPercent`.
- **Backward compatibility:** N/A.
- **Verification plan:** Render with 76% → amber; 89% → amber; 90% → red.
- **Estimated complexity:** S.
- **Priority:** P1.

### F-A9 — Stale-snapshot visual state

- **Current behavior:** `MainViewModel.StatusText` says "Last update: 3:42 PM" with no color emphasis.
- **Problem:** If the extension dies and stops pushing, the widget will happily show 23-hour-old data with no warning beyond a small timestamp.
- **Recommended change:** Compute staleness = `now - snapshot.Ts`. If > 2 × user's configured refresh interval, render status strip in `Mocha.Peach`; if > 6 ×, in `Mocha.Red` + dim each ring to 50% opacity. Show "STALE — last update Xh ago" text.
- **Code locations:** `MainViewModel.cs` (compute), `BucketViewModel.cs` (`IsStale` property), `MainWindow.xaml` (bind opacity + color).
- **Backward compatibility:** N/A.
- **Verification plan:** Manually rewind the snapshot file's `ts` field by 2 h; observe staleness band.
- **Estimated complexity:** S.
- **Priority:** P1.

### F-A10 — Log rotation / size cap

- **Current behavior:** `Logger.cs` appends forever to `nmh-{date}.log`. Files are date-bucketed but never deleted.
- **Problem:** Multi-month users accumulate dozens of MB of logs.
- **Recommended change:** On `Init`, delete log files in `AppPaths.LogsDir` whose name parses to a date older than 14 days. Size-cap individual files at 10 MB (rotate to `nmh-{date}.log.1` then truncate).
- **Code locations:** `src/QuotaGlass.NMH/Logger.cs`.
- **Backward compatibility:** N/A.
- **Verification plan:** Manually drop 30 fake-dated log files into `LogsDir`; run NMH; confirm ≤14 remain.
- **Estimated complexity:** S.
- **Priority:** P2.

### F-A11 — Replace `aaaa…aaaa` placeholder + Firefox manifest with real values

- Folds into F-A2 + F-A3 — listed separately so it can't be lost in review.
- **Priority:** P0.

### F-A12 — Schema versioning + migration scaffold

- **Current behavior:** `BucketSnapshot.SchemaVersion = 1` is set but never read.
- **Problem:** When the schema changes in v0.3+, old `snapshot.json` files will deserialize-but-be-wrong. Need an explicit migration path now while the schema is empty.
- **Recommended change:** On widget startup, read `snapshot.json` raw, check `schemaVersion`; if older than supported, run upgrade chain or discard with a log entry. Pin the supported version range in `Shared/SchemaVersion.cs`.
- **Code locations:** New `Shared/SchemaVersion.cs`. `AtomicJsonFile.Read` callers (`SnapshotWatcher.ReloadAndPublish`).
- **Backward compatibility:** First migration is a no-op; sets the pattern.
- **Verification plan:** Inject a fake `snapshot.json` with `schemaVersion: 0`; confirm widget treats it as missing and logs a one-line warning.
- **Estimated complexity:** S.
- **Priority:** P1.

### F-A13 — NMH ack should include schema version + server time

- **Current behavior:** `MessagePump.WriteAckAsync` (line 109) emits `{"ok":true,"detail":"ok"}`.
- **Problem:** Extension can't tell whether the NMH it's talking to supports a newer schema. Versions will drift.
- **Recommended change:** Ack becomes `{"ok":true,"detail":"ok","nmhVersion":"0.1.0","schemaMin":1,"schemaMax":1,"serverTime":"..."}`. Extension reads, can adapt.
- **Code locations:** `MessagePump.cs` `WriteAckAsync`.
- **Backward compatibility:** Extra fields; clients ignore unknown.
- **Verification plan:** Synthetic JSON-write test on `MessagePump`.
- **Estimated complexity:** XS.
- **Priority:** P1.

### F-A14 — Origin allow-list enforcement

- **Current behavior:** `MessagePump.RunAsync` accepts the origin via `callerOrigin` arg but never checks it against the registered `allowed_origins` list before processing messages.
- **Problem:** If a malicious local extension registered a manifest claiming our host name (it can't if our HKCU key is set, but an HKLM key from an installer could shadow ours), the NMH would happily accept its messages.
- **Recommended change:** Hardcode the same `allowed_origins` list used by `HostRegistrar`. In `MessagePump`, reject all messages with `{"ok":false,"detail":"origin-rejected"}` if `callerOrigin` doesn't match.
- **Code locations:** `MessagePump.cs` constructor + `HandleMessageAsync`. Extract `AllowedOrigins.cs` constant array shared with `HostRegistrar`.
- **Backward compatibility:** N/A.
- **Verification plan:** Manually launch `QuotaGlass.NMH.exe "chrome-extension://fake-id/"` and pipe a JSON frame; assert origin-rejected reply.
- **Estimated complexity:** S.
- **Priority:** P1.

### F-A15 — Bucket card click → open analytics

- Already proposed as F-N6 (new feature). Listed here for completeness because it's the natural improvement to F-03 widget.

### F-A16 — `dotnet test` project for the four highest-risk seams

- **Current behavior:** Zero tests in the solution.
- **Problem:** Atomic write, message-pump framing, countdown formatting, and ring percent→sweep math are all easy to break and hard to spot visually.
- **Recommended change:** New `test/QuotaGlass.Tests/` xUnit project. Initial tests:
  - `AtomicJsonFile_Roundtrip` (write then read returns same object).
  - `AtomicJsonFile_PartialWriteDoesNotReplaceExisting` (kill mid-write, confirm old file intact).
  - `MessagePump_FrameDecode_HappyPath` (4-byte LE + JSON → BucketSnapshot).
  - `MessagePump_RejectsOversizeFrame` (length > 1 MB → write-failed ack).
  - `MessagePump_RejectsTruncatedPayload`.
  - `BucketViewModel_CountdownFormatting` (Δ in days / hours / minutes / seconds / "renewed").
  - `RadialRing_PercentToSweep` (0% → no sweep; 100% → full circle; 50% → 180°).
  - `HostRegistrar_ManifestSerialization` (allowed_origins array shape matches Chrome schema).
- **Code locations:** New `test/QuotaGlass.Tests/`.
- **Backward compatibility:** N/A.
- **Verification plan:** `dotnet test` passes.
- **Estimated complexity:** M.
- **Priority:** P1.

### F-A17 — Make `BucketViewModel.TickCountdown` only raise INPC when the formatted string actually changed

- **Current behavior:** Every second, every visible bucket raises `PropertyChanged("TimeUntilResetLabel")`, forcing WPF to re-evaluate the binding even though for a "4h 12m" countdown the displayed string only changes once per minute.
- **Problem:** Cheap-but-pointless work. 6 buckets × 1 Hz = 6 needless re-renders per second.
- **Recommended change:** Cache the last formatted string in `BucketViewModel`; only raise INPC when it differs. Same for `Percent` (only changes on snapshot ingest, not on tick).
- **Code locations:** `BucketViewModel.cs` `TickCountdown`.
- **Backward compatibility:** N/A.
- **Verification plan:** Hook a counter to the `PropertyChanged` event; observe ticks per minute.
- **Estimated complexity:** XS.
- **Priority:** P2.

### F-A18 — Atomic write should fsync before rename

- **Current behavior:** `AtomicJsonFile.Write` calls `File.WriteAllText(tmp, ...)` then `File.Replace`. No `FileStream.Flush(true)` between them.
- **Problem:** A power-cut between write and rename can leave both the original snapshot.json and the tmp file in inconsistent state. Low likelihood, but the atomic-write pattern's whole point is to defend against this.
- **Recommended change:** Replace with `using var fs = new FileStream(tmp, FileMode.Create); fs.Write(bytes); fs.Flush(flushToDisk: true);` before the rename.
- **Code locations:** `src/QuotaGlass.Shared/AtomicJsonFile.cs`.
- **Backward compatibility:** N/A.
- **Verification plan:** Already covered by `F-A16` partial-write test.
- **Estimated complexity:** XS.
- **Priority:** P2.

### F-A19 — Theme palette uses Catppuccin Mocha brushes but the `WindowChromeBorder`'s actual `Background` is bound to `Brush.Window.Background` with `Opacity=0.92`, applied to a `SolidColorBrush` whose Color is `Mocha.Base` — there is no actual Mica/Acrylic backdrop

- Already filed as F-N5. Listed here for completeness.

### F-A20 — README "Install" section currently says "v0.1.0 has not shipped yet"

- **Current behavior:** README lines 89–100 contain placeholder text.
- **Problem:** First-time visitor to the repo can't tell what to do.
- **Recommended change:** Once v0.1.0 actually ships, replace with real steps. Until then, swap for an explicit "Status: pre-release scaffold" callout pointing to ROADMAP.md.
- **Code locations:** `README.md`.
- **Backward compatibility:** N/A.
- **Verification plan:** Render README in GitHub preview, confirm no placeholder.
- **Estimated complexity:** XS.
- **Priority:** P1.

### F-A21 — Correct the OSS-landscape claim in `docs/research.md`

- **Current behavior:** `docs/research.md` §5 claims "There is no Windows-native floating widget for Claude + Codex usage as of May 2026."
- **Problem:** Verified false. C-01..C-04 + 2 more all exist.
- **Recommended change:** Rewrite §5 to reflect the actual landscape (use this report's §5 as source of truth). Position QuotaGlass as "the only Windows tracker for browser-session users" + "the only one with an alarm ladder + custom-audio surface."
- **Code locations:** `docs/research.md` §5.
- **Backward compatibility:** N/A.
- **Verification plan:** Reviewer reads new §5; concurs.
- **Estimated complexity:** S.
- **Priority:** P1 (positioning affects messaging in README + release notes).

---

## Reliability, Security, Privacy, and Data Safety

### Bugs found

- **R-Bug-01** — Firefox extension ID mismatch (F-A3). Verified.
- **R-Bug-02** — Bucket schema mismatch (F-A1). Verified.
- **R-Bug-03** — `×` quits the process (F-A7). Verified by reading `App.xaml` line 5.
- **R-Bug-04** — `RadialRing.OnRender` creates new `Pen` objects every frame, then calls `Freeze()`. Cheap, but two heap allocations per frame * however many cards. Trivial polish; not high priority.

### Missing guardrails

- **R-Sec-01** — Native-messaging origin not enforced (F-A14). Verified.
- **R-Sec-02** — No length cap on `kind` or other string fields in the inbound JSON. A malicious payload with a 950 KB `label` field would pass the 1 MB frame check and force the widget to render that string. Mitigation: cap string field lengths in deserialization or render.
- **R-Sec-03** — `Process.Start` calls (if/when F-N6 adds them) need to be URL-scheme-restricted (`http`/`https` only) to avoid weaponizing the bucket card click into a `file://` or `shell:` link. Easy if implemented carefully now.

### Permission / network / filesystem concerns

- **R-Net-01** — QuotaGlass v0.1 makes **zero outbound network calls**. The "nothing leaves your machine" promise in the README is currently true and easy to keep. When F-N1 (direct credential reading) lands, calls to `api.anthropic.com` become outbound — must update README privacy section to be specific.
- **R-FS-01** — `%LOCALAPPDATA%\QuotaGlass\` is user-writable (per-user install). No need for elevation. Confirmed.
- **R-Reg-01** — `HostRegistrar` writes 4 HKCU keys. All under `Software\*\NativeMessagingHosts\com.sysadmindoc.quotaglass` — namespaced; cannot collide with other apps. `--unregister` cleans up all 4. Confirmed.

### Recovery and rollback

- **R-Rec-01** — No "factory reset" affordance. If `settings.json` gets corrupted, user has no in-widget way to wipe and start over. Add a Settings → Reset to defaults button when N-15 settings panel lands.
- **R-Rec-02** — `--unregister` only clears registry keys, not snapshot.json / settings.json / logs. Add `--purge` flag that also clears `%LOCALAPPDATA%\QuotaGlass\*`.

### Logging / diagnostics

- **R-Log-01** — Logger has no rotation (F-A10). Verified.
- **R-Log-02** — No correlation IDs across NMH ↔ Widget. When debugging "why didn't the widget update," there's no easy way to match a specific NMH log line to a specific Widget reload. Add a 4-char request ID on every NMH inbound frame, propagate through to the snapshot.json `lastRequestId`, log it in the Widget watcher.
- **R-Log-03** — Widget has no log file at all. Add `widget-{date}.log` mirroring NMH's logger pattern.

---

## UX, Accessibility, and Trust

### Onboarding gaps

- **UX-Onb-01** — No first-run guidance (covered by F-N3).
- **UX-Onb-02** — Installer (when it exists) should run `--register` automatically. User should not have to know about a CLI.
- **UX-Onb-03** — "Where do I get the extension?" needs a one-click affordance in the Setup card.

### Empty / loading / error / disabled states

- **UX-Sta-01** — "Waiting for first snapshot from extension…" is the only handled state. Need: stale, error (NMH disconnected), partial (one provider OK, other failed), zero-state (bucket at 100%), reset-in-progress.
- **UX-Sta-02** — When `snapshot.json` exists but contains `Snapshot.Claude = null` (e.g., user only has Codex working), the widget should show only Codex cards, not blow up. Verify reconciler handles this.

### Destructive / irreversible actions

- **UX-Dest-01** — `--unregister` is one-shot, no confirmation. If exposed via Settings UI, prompt before running.
- **UX-Dest-02** — Custom-sound picker (when implemented) should copy the source file into `%LOCALAPPDATA%\QuotaGlass\sounds\`, not symlink — original file deletion would break alarms. Already documented in `docs/research.md` §6.

### Settings clarity

- **UX-Set-01** — When the settings panel lands, every toggle should pair with a one-sentence "what does this do" tooltip. Take Maciek-roboblog's "calm summary at 08:00" microcopy style as a template.

### Accessibility

- **UX-Acc-01** — RadialRing has no `AutomationProperties.Name`. Screen readers see nothing. Add `AutomationProperties.Name="{Binding Provider} {Binding Label}, {Binding PercentLabel}, {Binding ResetAtLabel}"` on the card border.
- **UX-Acc-02** — No keyboard navigation. The widget is mouse-only today. Add Tab focus + Enter activation on card → open analytics.
- **UX-Acc-03** — `prefers-reduced-motion` is not honored (the spinning ring is currently static, so OK for now, but the planned alarm-fire animation needs to respect it).
- **UX-Acc-04** — Catppuccin Mocha contrast: the `Brush.Card.MutedText` (Overlay1 `#7F849C`) on `Brush.Card.Background` (Mantle `#181825` @ 0.88 opacity) has a contrast ratio of approximately 4.2:1 — below WCAG AA 4.5:1 for normal text. Bump muted to Overlay0 (`#6C7086`) or lighter.

### Microcopy and trust signals

- **UX-Cop-01** — Status text "Last update: 3:42 PM" should also say "(via extension)" or "(direct API)" so the user knows where their data is coming from. Surface source in v0.2.
- **UX-Cop-02** — Empty state copy should explicitly say "No data leaves your machine; QuotaGlass talks only to your local AI-Usage_Tracker extension." Trust-building.

---

## Architecture and Maintainability

### Module / boundary improvements

- **Arch-01** — `QuotaGlass.Shared` should not depend on WPF or Windows-only APIs. Currently it doesn't (verified). Keep it so when F-N1 adds the direct-credential poller, that code goes in NMH not Shared.
- **Arch-02** — `Services/SnapshotWatcher` and the future `Services/AlarmScheduler` should both live behind an `ISnapshotProvider` interface. Then the widget can be exercised in tests with a fake provider.
- **Arch-03** — `App.xaml.cs` is essentially empty. When tray icon (F-N4) + Velopack (F-N2) + CLI arg handling (F-N8) land, this file becomes the orchestration root and should compose those services via a tiny manual DI block, not magic-Locator.

### Refactor candidates

- **Arch-04** — `HostRegistrar.cs` has 4 near-identical `WriteRegistryKey` calls for the 4 browser families. Extract a `Browser` enum + table of `(RegistryRoot, ManifestKind)`. Reduces drift when adding a 5th browser.
- **Arch-05** — `MainWindow.xaml` style definitions for `HitButton` should move into `Theme/Controls.xaml` to keep XAML lean. One-time refactor before the settings panel adds 10 more buttons.

### Test gaps

- See F-A16 — first test project needed.
- No integration test that exercises the full pipe (NMH stdin → snapshot.json → widget reads). Add a powershell-driven end-to-end test post-v0.1.

### Documentation gaps

- See F-N9 — extension integration spec.
- See F-A20 — README install section.
- See F-A21 — research dossier accuracy.
- No `CONTRIBUTING.md` (mirror AI-Usage_Tracker roadmap `N-22`).
- No `SECURITY.md` for responsible disclosure.

### Release / build / deployment gaps

- No `.github/workflows/` directory yet (roadmap `N-18`).
- No `Directory.Packages.props` for central package management — fine at 1 package, but worth adding when count grows.
- No code signing plan. Document an explicit decision: "v0.1.x is unsigned; users will see SmartScreen prompt; we accept this." OR get a sigstore-style signing setup.

---

## Prioritized Roadmap

This is the implementation-ready ordering. Each item points at QuotaGlass files; companion items in AI-Usage_Tracker are explicitly called out.

### Phase 0 — Unblock end-to-end (must land before any v0.1.0 release)

- [ ] **P0 — F-N9 — Author `docs/extension-integration.md` schema spec**
  - Why: Pins the contract before code is written against it; prevents drift between BucketSnapshot.cs and the extension.
  - Evidence: §3.1 schema mismatch; existing scattered references.
  - Touches: `docs/extension-integration.md` (new).
  - Acceptance: Doc contains JSON envelope + every field type + fixture JSON + schema-bump policy.
  - Verify: Reviewer can produce a v1 snapshot.json from the doc alone.

- [ ] **P0 — F-A1 — Rewrite BucketSnapshot to mirror extension shape**
  - Why: Without this, deserialization silently zeros every percent value; no end-to-end works.
  - Evidence: §3.1 + §8 F-A1 field map.
  - Touches: `src/QuotaGlass.Shared/BucketSnapshot.cs`, `src/QuotaGlass.Widget/ViewModels/{Bucket,Main}ViewModel.cs`, `src/QuotaGlass.NMH/MessagePump.cs`.
  - Acceptance: A fixture JSON straight from `AI-Usage_Tracker` `loadState()` deserializes into the new types with every field populated.
  - Verify: `dotnet test` round-trip assertion (introduced via F-A16).

- [ ] **P0 — F-A2 — Pin Chrome extension ID via manifest `"key"`**
  - Why: Native messaging `allowed_origins` cannot wildcard; without a pinned key every user's extension gets a different ID.
  - Evidence: Chrome NMH docs verified; chrome.json read.
  - Touches: `~/repos/AI-Usage_Tracker/manifests/chrome.json`, `~/repos/QuotaGlass/src/QuotaGlass.NMH/HostRegistrar.cs`.
  - Acceptance: Reloading the unpacked extension yields a fixed Chrome ID equal to the value hardcoded in HostRegistrar.
  - Verify: `chrome://extensions/` → ID column matches.

- [ ] **P0 — F-A3 — Fix Firefox extension ID typo in HostRegistrar**
  - Why: Wrong ID rejects all Firefox connections.
  - Evidence: `manifests/firefox.json` line 12 vs `HostRegistrar.cs` line 24.
  - Touches: `src/QuotaGlass.NMH/HostRegistrar.cs`.
  - Acceptance: Firefox `browser.runtime.connectNative` connects without error.
  - Verify: Manual test in Firefox Developer Edition.

- [ ] **P0 — F-A4 — Implement bridge.js with persistent port + reconnect + 25 s ping**
  - Why: Fire-and-forget post pattern triggers MV3 30 s idle termination; documented bug pattern.
  - Evidence: Chrome SW lifecycle docs + issue #2688 + claude-code#16350.
  - Touches: `~/repos/AI-Usage_Tracker/src/lib/bridge.js` (new), `background.js` (hook), `manifests/{chrome,firefox}.json` (add `nativeMessaging` permission).
  - Acceptance: `chrome://serviceworker-internals/` shows AUT SW staying alive across multiple ping intervals.
  - Verify: 15 min idle observation.

- [ ] **P0 — F-N8 — Add `--inject-fake-snapshot` dev mode**
  - Why: Without it, widget work requires the full extension+NMH+browser chain to test.
  - Evidence: Standard dev-ergonomics pattern; nothing else allows solo widget iteration.
  - Touches: `App.xaml.cs`, new `Services/FakeSnapshotInjector.cs`.
  - Acceptance: `dotnet run --project src/QuotaGlass.Widget -- --inject-fake-snapshot` shows 4 bucket cards.
  - Verify: Manual launch.

- [ ] **P0 — F-A7 — Close button hides, not quits**
  - Why: Closes-to-quit is a first-run footgun; user loses widget with no recovery path.
  - Evidence: `App.xaml` line 5 + `MainWindow.xaml.cs` `OnCloseClick`.
  - Touches: `App.xaml`, `MainWindow.xaml.cs`. Optional global hotkey until F-N4 lands.
  - Acceptance: Clicking × hides the window; process remains in `tasklist`.
  - Verify: Manual.

- [ ] **P0 — F-N4 — System tray icon with show/hide/refresh/quit menu**
  - Why: F-A7 is incomplete without a way to bring the widget back.
  - Evidence: §6 competitive landscape — every competitor has this.
  - Touches: New `Services/TrayIconService.cs`. New tray icon PNGs in `assets/`. `App.xaml.cs` registration.
  - Acceptance: Tray icon visible; right-click menu works; double-click toggles widget.
  - Verify: Manual.

- [ ] **P0 — F-A6 — Spike custom-audio playback on Windows 11 26100**
  - Why: Toast custom-audio approach is unverified for unpackaged WPF; alarm UX hinges on it.
  - Evidence: Microsoft docs disagree across legacy vs new APIs.
  - Touches: One-off scratch test; findings update `docs/research.md` §6.
  - Acceptance: Documented either "file:/// works" or "fallback to SoundPlayer alongside silent toast."
  - Verify: Hear the WAV play.

- [ ] **P0 — F-N3 — In-widget Setup Checklist**
  - Why: Empty state today is a dead end; first-run users need guidance.
  - Evidence: No existing empty-state docs; only state handled is "Waiting…".
  - Touches: New `Views/SetupCard.xaml`. `MainViewModel.Mode`. New `Services/HealthCheck.cs`.
  - Acceptance: Fresh-machine run shows 3-step checklist; each step becomes ✅ when satisfied.
  - Verify: Manual on a VM.

### Phase 1 — v0.1.0 ship-ready

- [ ] **P1 — F-A14 — Origin allow-list enforcement in MessagePump**
  - Why: Defense in depth; cheap to add now.
  - Evidence: §10 R-Sec-01.
  - Touches: `MessagePump.cs`. Extract `AllowedOrigins.cs`.
  - Acceptance: NMH rejects forged origin with `{"ok":false,"detail":"origin-rejected"}`.
  - Verify: F-A16 unit test.

- [ ] **P1 — F-A13 — NMH ack includes schema range + version**
  - Why: Forward-compat handshake; cheap to ship in v0.1.
  - Evidence: §8 F-A13.
  - Touches: `MessagePump.WriteAckAsync`.
  - Acceptance: Ack JSON contains `nmhVersion`, `schemaMin`, `schemaMax`, `serverTime`.
  - Verify: F-A16 unit test.

- [ ] **P1 — F-A12 — Schema versioning + migration scaffold**
  - Why: Pin the policy before the schema grows.
  - Evidence: §8 F-A12; schemaVersion field currently set-but-unused.
  - Touches: New `Shared/SchemaVersion.cs`. `SnapshotWatcher`.
  - Acceptance: Outdated-schema file logs a warning and is treated as missing.
  - Verify: F-A16 unit test.

- [ ] **P1 — F-A8 — Honor extension's 75/90 thresholds in ring color ramp**
  - Why: Visual consistency between browser ring and desktop ring.
  - Evidence: Extension CSS uses 75/90; ours uses 60/85.
  - Touches: `RadialRing.cs`, `BucketViewModel.cs`, settings file.
  - Acceptance: Render at 76% → amber, 89% → amber, 90% → red.
  - Verify: F-A16 unit test.

- [ ] **P1 — F-A9 — Stale-snapshot visual state**
  - Why: User must be able to see that data is old.
  - Evidence: §8 F-A9.
  - Touches: `MainViewModel`, `BucketViewModel.IsStale`, `MainWindow.xaml`.
  - Acceptance: Rewind snapshot ts by 2 h → ring dims + status colors.
  - Verify: Manual (until F-A16 covers UI).

- [ ] **P1 — F-N6 — "Open analytics" deep-link on card click**
  - Why: Re-uses the extension's existing aut/open-analytics handler.
  - Evidence: `AI-Usage_Tracker/src/background.js` lines 219–229.
  - Touches: `MainWindow.xaml` ItemTemplate, `MainWindow.xaml.cs`.
  - Acceptance: Card click opens correct URL in default browser.
  - Verify: Manual.

- [ ] **P1 — F-A16 — Test project with 8 initial tests**
  - Why: Lock in the fixed behaviors of F-A1, F-A2, F-A12, F-A13, F-A14, F-A8 before adding alarm scheduler complexity.
  - Evidence: §15 zero existing tests.
  - Touches: New `test/QuotaGlass.Tests/`.
  - Acceptance: `dotnet test` green.
  - Verify: CI on PR (after N-18).

- [ ] **P1 — F-N2 — Velopack installer + auto-update**
  - Why: Replaces planned Inno Setup; adds free auto-update; reduces release friction.
  - Evidence: §6 competitive landscape; `vpk` docs.
  - Touches: New `installer/` config. `App.xaml.cs` adds `VelopackApp.Build().Run()`. Release workflow.
  - Acceptance: v0.1.0 installs, v0.1.1 published, v0.1.0 auto-updates to v0.1.1 on launch.
  - Verify: Manual upgrade test against a local feed.

- [ ] **P1 — Roadmap N-12 — Toast notification adapter**
  - Why: Required for alarm UX.
  - Evidence: ROADMAP.md.
  - Touches: New `Services/ToastService.cs`. Wraps `ToastContentBuilder` (or SoundPlayer fallback per F-A6 outcome).
  - Acceptance: Synthetic alarm fire produces a Windows toast with title, body, audio.
  - Verify: Manual.

- [ ] **P1 — Roadmap N-13 — Alarm-ladder scheduler**
  - Why: Core "renewal alarm" feature in the brief.
  - Evidence: Brief + ROADMAP.md.
  - Touches: New `Services/AlarmScheduler.cs`. Ties into `Services/ToastService`.
  - Acceptance: Mock a bucket with `resetISO` 24h+1min away; scheduler queues 24h/12h/6h/3h/1h/30m/15m/5m/at-reset toasts; each fires once.
  - Verify: Mock clock + unit test.

- [ ] **P1 — Roadmap N-14 — Zero-state R3 toast**
  - Why: Brief explicitly asks for "rings notification sound when usage hits 0%."
  - Evidence: Brief.
  - Touches: `AlarmScheduler` + new rule R3.
  - Acceptance: Bucket flipping to `percentUsed >= 100` triggers a distinct toast once per resetISO.
  - Verify: F-A16 unit test on rule evaluator.

- [ ] **P1 — Roadmap N-15/N-16 — Settings panel + persistence**
  - Why: Refresh interval, alarm ladder toggles, custom sound, themes — all in scope for v0.1.
  - Evidence: Brief.
  - Touches: New `Views/SettingsPanel.xaml`, `ViewModels/SettingsViewModel.cs`. `AppPaths.SettingsFile`.
  - Acceptance: Toggle a ladder tier off; reload widget; tier still off.
  - Verify: Manual + F-A16 round-trip test on Settings JSON.

- [ ] **P1 — Roadmap N-17 (revised) — Velopack-built installer**
  - Why: Replaces Inno Setup; subsumes F-N2.
  - Evidence: §6 + F-N2.
  - Touches: `installer/` scripts. Release workflow.
  - Acceptance: `QuotaGlass-Setup-v0.1.0.exe` installs widget, NMH, runs `--register` post-install, drops Start Menu shortcut.
  - Verify: Manual install on a fresh VM.

- [ ] **P1 — Roadmap N-18 — GitHub Release workflow**
  - Why: Required for reliable distribution + auto-update feed.
  - Evidence: ROADMAP.md.
  - Touches: New `.github/workflows/release.yml`. `workflow_dispatch` trigger.
  - Acceptance: Manual dispatch builds, packs with vpk, uploads to GitHub Release.
  - Verify: Run via `gh workflow run release.yml -f tag=v0.1.0`.

- [ ] **P1 — Roadmap N-19/N-20/N-21 — Real install docs + screenshots + integration spec**
  - Why: Docs gate user adoption.
  - Touches: `README.md`, `assets/screenshots/`, `docs/extension-integration.md`.
  - Acceptance: README has shipping install steps + a hero screenshot + a popup screenshot + a toast screenshot.
  - Verify: GitHub render of README.

- [ ] **P1 — F-A20 — README "Install" placeholder replaced**
  - Folds into the N-19 task above.

- [ ] **P1 — F-A21 — `docs/research.md` §5 corrected re: existing Windows competitors**
  - Folds into the docs phase.

### Phase 2 — v0.2.0 polish + true differentiator

- [ ] **P1 — F-N1 — Direct credential reading (`~/.claude/.credentials.json` + `~/.codex/auth.json`)**
  - Why: Lifts QuotaGlass from "browser-only" to "browser + CLI" coverage. Strongest competitive move.
  - Evidence: §6 — every Windows competitor surveyed already does this.
  - Touches: New `QuotaGlass.NMH.CredentialPoller`. New `Shared/ClaudeCodeCredentials.cs`. New `Shared/CodexCredentials.cs`. Settings file gains `poll.{claudeCreds,codexCreds}.enabled` + cadence. Task Scheduler / Windows Service registration in installer.
  - Acceptance: Uninstall extension; widget keeps updating from creds.
  - Verify: Manual.

- [ ] **P1 — F-N5 — Mica / Acrylic backdrop on Win11**
  - Why: Visual polish; today's "glass" is a translucent solid color.
  - Touches: New `Services/SystemBackdropService.cs`. `MainWindow.xaml.cs`.
  - Acceptance: On Win11 22621+, widget picks up wallpaper through Mica.
  - Verify: Manual side-by-side.

- [ ] **P2 — Roadmap NX-04 — Edge-snap on drag**
- [ ] **P2 — Roadmap NX-05 — Multi-monitor placement memory**
- [ ] **P2 — Roadmap NX-06 — Catppuccin Latte light theme**
- [ ] **P2 — Roadmap NX-07 — Reduced-motion mode**
- [ ] **P2 — Roadmap NX-08 — Sparkline history panel**
- [ ] **P2 — Roadmap NX-09 — Tooltip on ring hover**
- [ ] **P2 — Roadmap NX-10 — Embedded log panel**
- [ ] **P2 — F-N10 — ARM64 build target**
- [ ] **P2 — F-A4-Bug — `RadialRing` pen freezing minor**
- [ ] **P2 — F-A10 — Log rotation**
- [ ] **P2 — F-A17 — INPC only on changed string**
- [ ] **P2 — F-A18 — `Flush(true)` before atomic rename**
- [ ] **P2 — R-Rec-02 — `--purge` flag**

### Phase 3 — v0.3+ power features

- [ ] **P2 — F-N7 — Shell-command webhook on alarm fire**
- [ ] **P2 — Roadmap L-01 — Per-tier alarm sound + message**
- [ ] **P2 — Roadmap L-02 — 7-day "next resets" calendar view**
- [ ] **P2 — Roadmap L-04 — Action Center deep-links**
- [ ] **P2 — Roadmap L-06 — Named pipe between NMH and Widget**
- [ ] **P2 — Roadmap L-07 — Plan auto-detection from reset cadence**
- [ ] **P2 — Roadmap L-08 — Pace marker on rings**
- [ ] **P2 — Roadmap L-09 — Anomaly / spike detection**
- [ ] **P2 — Roadmap L-10 — Provider plugin contract**
- [ ] **P3 — Roadmap L-03 — Win11 Widgets board investigation**
- [ ] **P3 — Roadmap UC-01 — Avalonia port (Linux + macOS)**

### Phase 4 — accessibility + trust hardening

- [ ] **P1 — UX-Acc-01..04 batch** — AutomationProperties, keyboard nav, reduced motion, AA contrast review.
- [ ] **P2 — UX-Cop-01/02** — Source-of-data label + trust microcopy.
- [ ] **P2 — `CONTRIBUTING.md` + `SECURITY.md`**.
- [ ] **P2 — Code-signing decision documented**.

---

## Quick Wins

Low-risk changes that can be completed quickly (each ≤30 min):

1. **F-A3** — Fix Firefox extension ID typo (1 line).
2. **F-A7** — `Close()` → `Hide()` + `ShutdownMode="OnExplicitShutdown"` (3 line change).
3. **F-A8** — Move ring thresholds to constants, change defaults to 75/90 (10 lines).
4. **F-A13** — Extend `WriteAckAsync` payload (5 lines).
5. **F-A17** — Cache last formatted countdown string (5 lines).
6. **F-A18** — `using var fs = ...; fs.Flush(true);` (5 lines).
7. **F-A20** — Replace README install placeholder (paragraph rewrite).
8. **F-A21** — Update `docs/research.md` §5 (paragraph rewrite).
9. **R-Sec-02** — Cap inbound string field lengths in JSON deserialization (10 lines).
10. **R-Log-03** — Add `widget-{date}.log` mirroring NMH logger (~30 lines).

---

## Larger Bets

Bigger features or refactors that need planning, design, or staged rollout:

1. **F-A1 + F-N9 — Schema spec + BucketSnapshot rewrite.** Touches every layer; gates everything. Do them together.
2. **F-A2 — Pin Chrome extension ID.** Requires generating + safely storing the keypair; can't be undone without breaking installed users (none yet, so do it now).
3. **F-N1 — Direct credential reading.** New long-running process model; new failure modes; new privacy claims to validate.
4. **F-N2 — Velopack migration.** Affects the entire distribution + update story; one-time investment with big leverage.
5. **F-N3 + F-N4 — Setup Checklist + tray icon.** Together they define the "first-run + ongoing" UX; design them together.
6. **N-12/N-13/N-14 — Toast adapter + alarm scheduler + zero-state.** The core alarm feature in the brief; testable but needs careful timer + idempotency design.

---

## Explicit Non-Goals

Ideas considered and rejected, with reasons:

- **Telemetry / opt-in usage analytics.** Conflicts with the "nothing leaves your machine" promise. Same conclusion as AI-Usage_Tracker UC-02.
- **Mobile companion (Android/iOS).** Requires a server to push extension snapshots over the internet, breaking the privacy story. Sister-project scope at best.
- **Windows 7/8 / Win10 < 17763 support.** WPF + .NET 9 + toast XML targets are clean from 1809+. Earlier OSes are end-of-life.
- **Reading Chrome cookies directly to call the APIs ourselves (bypassing the extension).** Tracked as anti-recommendation in `docs/research.md` §3 Option B. Chromium changed cookie encryption mid-2024; maintenance treadmill. F-N1's credential-file approach is strictly better.
- **A "block API calls when over quota" feature.** Same hard-skip as AI-Usage_Tracker UC-10. Risky UX, prone to false fires, easy to game.
- **Confetti / celebratory animation on reset.** Same R-03 reject in both projects — restrained design language.
- **Pill / oval / fully-rounded backdrop UI anywhere.** Hard-banned by the global project rule. No exceptions.

---

## Open Questions

Only blockers; everything else has a recommended default and can be decided during implementation.

1. **Should F-A2 keep the original AI-Usage_Tracker manifest unmodified and instead use a "browser specific settings + key from settings page" pattern at runtime?** This would avoid the "every existing extension install gets a new ID" migration. Trade-off: more complex install UX (user copies their installation's ID into a QuotaGlass settings field). **Default: just add the `"key"` to the manifest — the extension has very few users, and the one-time uninstall+reinstall is acceptable.** Confirm with the user before merging F-A2.

2. **Should the v0.1 installer auto-launch the widget on first install, or wait for user action?** Tokens4Breakfast / Zrnik both auto-launch. **Default: auto-launch with a one-time "QuotaGlass is now in your tray" toast, so the user doesn't think the installer did nothing.** Confirm.

3. **What's the policy for unsigned binaries triggering SmartScreen?** Code signing certificates are $200–500/yr. **Default: ship unsigned for v0.1.x with a clear "click 'More info' → 'Run anyway' once" note in the README; revisit signing when there's actual user demand.** Confirm.

4. **Does the AI-Usage_Tracker maintainer (= same person, but distinct project hat) accept adding `"nativeMessaging"` to the extension's permissions and shipping `src/lib/bridge.js`?** This is a real privacy-permission bump for existing extension users and warrants a CHANGELOG entry there. **Default: yes, but call it out explicitly in the AUT release notes for the version that adds it.**

---

*End of plan. Implementing agent should treat this document as the prioritized work queue; every item is traceable to an evidence source in §2.*
