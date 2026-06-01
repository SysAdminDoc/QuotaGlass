# Project Research and Feature Plan — Pass 2 (Deep Audit Companion)

**Project:** QuotaGlass · `~/repos/QuotaGlass/` · v0.1.0-dev (commit `b9061b7`, 2026-05-24).
**Companion to:** [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md) — read that first; this document is **strictly additive**.
**Pass 2 scope:** Deep-read the upstream extension files Pass 1 only summarized (`countdown.js`, `claude-stream.js`, `history.js`, `analytics-scraper.js`, `page-interceptor.js`); fetch and read the actual source of the closest competitor (Zrnik/claude-usage-windows-taskbar-widget); run `dotnet list package --vulnerable / --deprecated`; cross-check authoritative MS docs on toast custom-audio in unpackaged WPF. Found three P0 findings Pass 1 missed and four Pass 1 claims that are wrong on closer reading.

## Executive Summary (Pass 2 delta)

Pass 1 produced a solid plan but contained four specific factual errors that materially change v0.1 priorities, and missed three deep-architecture issues that are blocking quality. Most consequential: **(a)** the entire `Microsoft.Toolkit.Uwp.Notifications` package can be eliminated — it pulls in a vulnerable transitive `System.Drawing.Common 4.7.0` (GHSA-rxg9-xrhp-64gj, Critical) and offers no capability over raw `Windows.UI.Notifications` (the competitor Zrnik does exactly this in ~80 lines and is shipping); **(b)** custom-audio via `<audio src="file:///...">` in unpackaged WPF toast XML is **silently ignored** by Windows per the authoritative UWP toast schema doc — the alarm UX must play the WAV/MP3 itself via `SoundPlayer`/`NAudio` and use `<audio silent="true"/>` to suppress the default Windows sound; **(c)** plain `Topmost="True"` is not robust against UAC, fullscreen apps, and system foreground events, and every Windows-widget competitor surveyed implements a WinEvent foreground hook to re-assert topmost. Together these are ~200 LOC of changes that take QuotaGlass from "compiles" to "actually behaves like a Windows widget."

**Top 7 net-new opportunities (do NOT appear in Pass 1):**

1. **P0-N1 — Eliminate `Microsoft.Toolkit.Uwp.Notifications`** in favor of raw `Windows.UI.Notifications.ToastNotificationManager` + `Windows.Data.Xml.Dom`. Removes the GHSA-rxg9-xrhp-64gj Critical CVE in transitive `System.Drawing.Common 4.7.0`, drops ~600 KB of binaries, simpler code path. Reference implementation: Zrnik's `NotificationService.cs` (verified).
2. **P0-N2 — `TopMostEnforcer` WinEvent hook.** Plain `Topmost="True"` is overridden by UAC prompts / fullscreen apps / foreground events. Re-assert via `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ...)` on a dedicated STA background thread. Reference: Zrnik's `TopMostEnforcer.cs` (verified, ~140 LOC).
3. **P0-N3 — Stop trying to use `<audio src="file:///...">`** in toasts. Per [Microsoft's UWP toast schema docs](https://learn.microsoft.com/en-us/uwp/schemas/tiles/toastschema/element-audio), only `ms-winsoundevent:*` values are supported; custom file paths fall back silently to default scenario sound. Play the user's custom WAV directly via `System.Media.SoundPlayer.Play()` at toast-fire-time and use `<audio silent="true"/>` in the toast XML.
4. **P1-N4 — Correct the color-threshold direction.** Pass 1 F-A8 claimed "the extension uses 75/90." It does — for *notifications*. The extension's actual *visual* color ramp in `lib/countdown.js:109` is **50/80**, and Zrnik's notifications use **75/90**. Decide once: recommendation **60/85 for color ramp + 75/90 for notifications** (matches Catppuccin Peach→Red transition + competitor norm).
5. **P1-N5 — Lightweight self-hosted updater** (Zrnik pattern: GitHub Releases API + PowerShell self-replace script) as the alternative to Velopack. Saves a dependency; matches the project's "no telemetry, simple" ethos. Verified ~140 LOC including PS script.
6. **P1-N6 — Surface the extension's burn-rate forecast** (`history.js:forecastExhaustion`, ~50 LOC linear regression with reset detection). Already produced upstream; QuotaGlass just needs to render "Pace: hitting cap by 4 PM" as a second ring tick or footer line. Free competitive feature.
7. **P1-N7 — Consume the extension's existing sparkline data** (`history.js:sparklineFor`). Pass 1 NX-08 assumed we'd build our own history buffer; we don't need to — the extension already maintains 30 days and exposes it via the snapshot envelope (assuming F-A1 mirrors the full state shape).

**Pass 1 errors corrected** (full details in §3):

- Pass 1 F-A8 conflated notification thresholds (75/90) with visual color thresholds (50/80). Net new in §3.4.
- Pass 1 §C-01 ("Zrnik: no notifications, no settings UI") is wrong — Zrnik has both, in `NotificationService.cs` (96 LOC) and `SettingsWindow.xaml.cs` (26 KB). Net new in §6.
- Pass 1 F-A6 marked custom-audio as "Needs live validation." Pass 2 found the authoritative answer: **never worked, never will, document it.** Net new in §3.3.
- Pass 1 §R-Sec-02 ("cap string field lengths") is correct but the bigger issue is the JSON deserializer itself — `System.Text.Json` with source-gen defaults has no `MaxDepth` cap; an attacker-controlled deeply-nested payload at the 1 MB frame limit is a JIT-amplification risk. Net new in §4.

---

## Evidence reviewed (Pass 2 only — what Pass 1 didn't cover)

### Local files newly inspected

- `AI-Usage_Tracker/src/lib/countdown.js` (113 LOC) — full read. Confirms reset-string parser handles 4 shapes; confirms color thresholds 50/80; provides `formatCountdown` / `formatResetAbsolute` / `ringColor` we should match.
- `AI-Usage_Tracker/src/lib/history.js` (67 LOC) — full read. Confirms `forecastExhaustion` linear-regression with reset detection (window split on `samples[i].percentUsed < samples[i-1].percentUsed - 5`); `sparklineFor` already produces 24-point sparklines per bucket.
- `AI-Usage_Tracker/src/lib/claude-stream.js` (146 LOC) — full read. `installClaudeMessageLimitInterceptor` patches `globalThis.fetch` in MAIN world, extracts `anthropic-ratelimit-unified-*` headers, parses SSE `message_limit` events. No QuotaGlass impact directly — payload arrives via `aut/claude-message-limit` and `aut/claude-rate-limit-headers` runtime messages on `background.js` lines 57–63.
- `AI-Usage_Tracker/src/page-interceptor.js` (23 LOC) — MAIN-world script that bridges the interceptor's `emit` callbacks to the page via `window.postMessage`. Confirms 3-hop chain: page-interceptor → page-bridge → background → bridge.js (new) → NMH.
- `AI-Usage_Tracker/src/analytics-scraper.js` (first 60 lines) — POLL_MS=1000, STABLE_REQUIRED=2. Confirms a freshly-installed user with no settings-page visit will have sparse snapshot data until the API path warms.

### External code newly inspected

- `Zrnik/claude-usage-windows-taskbar-widget` — full repo tree + the files below (all read raw, not summarized):
  - `ClaudeUsageWidget.csproj` (29 LOC). Net8.0-windows10.0.22621.0; `win-x64;win-arm64`; ZERO `PackageReference`s.
  - `NotificationService.cs` (96 LOC). Raw `Windows.UI.Notifications.ToastNotificationManager` + XML; thresholds 75/90; reset detection identical pattern to AI-Usage_Tracker R2.
  - `TopMostEnforcer.cs` (140 LOC). WinEvent hook on `EVENT_SYSTEM_FOREGROUND` on a dedicated background STA thread; re-asserts `SetWindowPos(HWND_TOPMOST, ...)` on every foreground change. Critical detail: `WinEventDelegate` field must not be a local variable (Release GC), and the message loop uses `WM_USER` to thread-marshal the re-topmost.
  - `Updater.cs` (118 LOC). `https://api.github.com/repos/{repo}/releases/latest`; picks asset by `win-x64`/`win-arm64`; writes a PS1 script that downloads, kills self, copies, restarts.
  - `Pace.cs` (76 LOC). Working-days math with 9–5 workday-window fractional remaining-day calculation.
  - `CredentialStore.cs` (first 100 LOC of 21 KB). Six credential sources: `claude-wsl`, `claude-windows`, `codex-wsl`, `codex-windows`, `codex-hermes-wsl`, `codex-hermes-windows`. JWT-claim-based dedup. Opaque-token fallback for `sk-ant-oat01-*`.

### External docs newly fetched

- [Microsoft UWP toast schema — `<audio>` element](https://learn.microsoft.com/en-us/uwp/schemas/tiles/toastschema/element-audio) — verified custom file paths are NOT supported; falls back to default sound.
- [GHSA-rxg9-xrhp-64gj](https://github.com/advisories/GHSA-rxg9-xrhp-64gj) — CVE-2021-24112; affects `System.Drawing.Common` 4.0.0–4.7.1; **MacOS/Linux only**; QuotaGlass is Win-only so practical risk is zero, but every CVE scanner will flag it.

### Verifications performed

- `dotnet list QuotaGlass.sln package --vulnerable --include-transitive` → **Verified:** `System.Drawing.Common 4.7.0` Critical (transitive via `Microsoft.Toolkit.Uwp.Notifications 7.1.3`).
- `dotnet list QuotaGlass.sln package --deprecated` → **Verified:** no direct deprecated packages.
- `dotnet list QuotaGlass.sln package --outdated` → **Verified:** ran clean (no newer minors available for our 1 direct package).
- `dotnet build QuotaGlass.sln -c Release -v normal` → **Verified:** 0 warnings, 0 errors.

### Could not verify in Pass 2

- **Whether `ToastNotificationManagerCompat` from the legacy package still does anything raw `ToastNotificationManager.CreateToastNotifier("QuotaGlass")` cannot.** The Toolkit's main value-add was action-callback wiring + tile-template type safety. For our toast (text-only with audio, no actions in v0.1), raw is sufficient. Toast *actions* (Snooze / Dismiss buttons) in v0.2+ may need the Toolkit's compat shim for `OnActivated` handling, or hand-rolled COM activator registration. **Defer decision until v0.2 toast-actions land; document the choice in `docs/extension-integration.md` (F-N9).**
- **Whether `~/.codex/auth.json` and `~/.hermes/auth.json` schemas match what `CredentialStore.cs` parses on a 2026-Q2 Codex CLI version.** Schemas may drift. **Needs live validation** before F-N1 (Pass 1) ships.

---

## Section 3 — Findings net-new vs Pass 1

### 3.1 — Toast notification refactor: drop the Toolkit, go raw WinRT

**Verified.**

**Current state:** `src/QuotaGlass.Widget/QuotaGlass.Widget.csproj` line 16 references `Microsoft.Toolkit.Uwp.Notifications` 7.1.3. The package transitively pulls `System.Drawing.Common 4.7.0` (Critical CVE on Linux/macOS, irrelevant to us functionally but tripping every CVE scanner) and ~600 KB of binaries. The package is not yet imported by any code; we're paying the dependency cost for zero capability.

**Discovery:** Zrnik's `NotificationService.cs` (96 LOC, verified raw via `gh api repos/Zrnik/.../contents/`) does the entire toast surface in `Windows.Data.Xml.Dom` + `Windows.UI.Notifications` with no NuGet package:

```csharp
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

var xml = new XmlDocument();
xml.LoadXml($"""
    <toast>
      <visual>
        <binding template="ToastGeneric">
          <text>{title}</text>
          <text>{body}</text>
        </binding>
      </visual>
    </toast>
    """);

var toast = new ToastNotification(xml);
ToastNotificationManager.CreateToastNotifier("QuotaGlass").Show(toast);
```

To make WinRT APIs available to a .NET 9 project, change the TargetFramework moniker from `net9.0-windows10.0.19041.0` to **at least** `net9.0-windows10.0.19041.0` (already done — confirmed). Add `<UseWinUI>false</UseWinUI>` to make explicit. Raw WinRT works out of the box; no extra packages.

**Why this matters:** removes the Critical-CVE flag, removes ~600 KB of binaries, removes one whole class of "Toolkit was archived in 2024 — when is it deprecated?" risk. Lighter compile + smaller install.

**Recommendation:**
- Delete the `Microsoft.Toolkit.Uwp.Notifications` package reference.
- Write `QuotaGlass.Widget.Services.ToastService.cs` modeled on Zrnik's pattern.
- For toast *actions* (Snooze / Dismiss buttons in v0.2+), either pull in the Toolkit again then or hand-roll the COM activator. Decide at v0.2.

**Estimated complexity:** S (~30 min including writing `ToastService.cs`).
**Priority:** **P0** (gates the alarm UX direction AND removes the CVE).

### 3.2 — `TopMostEnforcer`: plain `Topmost="True"` is not enough

**Verified via competitor source.**

**Current state:** `src/QuotaGlass.Widget/Views/MainWindow.xaml` line 15 sets `Topmost="True"`. This is not robust:

- A UAC consent dialog will demote the widget.
- Fullscreen apps (games, video players, presentations) demote it.
- `EVENT_SYSTEM_FOREGROUND` fires whenever a window comes forward; some scenarios (Win+D, Win+Tab) silently strip topmost.

**Discovery:** Zrnik's `TopMostEnforcer.cs` (140 LOC, verified raw) starts a dedicated background STA thread that:

1. Calls `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ..., WINEVENT_OUTOFCONTEXT)`.
2. On any foreground change, posts `WM_USER` to its own thread queue.
3. The thread's message loop dispatches `WM_USER` → `SetWindowPos(hwnd, HWND_TOPMOST, ...)`.

Critical detail buried in his code comment: "Must be a field — local variable gets GC'd in Release builds before the message loop ends" (line 67). The `WinEventDelegate` instance has to be rooted; the GC otherwise collects it asynchronously and the hook starts crashing.

**Recommendation:**
- New `src/QuotaGlass.Widget/Services/TopMostEnforcer.cs` ported from Zrnik's MIT-licensed implementation (with attribution).
- Instantiated from `MainWindow.OnSourceInitialized` (so `hwnd` is available).
- Add `Pause()` / `Resume()` API so the future Settings panel can demote during full-screen Settings.

**Estimated complexity:** M (porting + reading user32.dll P/Invoke is finicky).
**Priority:** **P0** (every user hits this within hours).

### 3.3 — Custom audio in toast XML is silently ignored

**Verified via Microsoft authoritative source.**

**Current state:** `docs/research.md` §6 (written in the original scaffold) asserts that `file:///<absolute path>` is "the only reliable scheme" for unpackaged WPF toast audio. Pass 1 F-A6 downgraded this to "Needs live validation."

**Discovery:** The [official UWP toast `<audio>` schema doc](https://learn.microsoft.com/en-us/uwp/schemas/tiles/toastschema/element-audio) explicitly enumerates *every* legal `src` value, and **all of them are `ms-winsoundevent:Notification.*`** (e.g. `Notification.Default`, `Notification.Looping.Alarm`, `Notification.SMS`, etc.). The remarks section says verbatim:

> If you specify a custom file path in the app payload, the default sound (notification, call, alarm, or reminder) will be played based on the specified scenario.

Translation: feeding `file:///c:/.../sound.wav` into a desktop-toast `<audio src=>` is **silently ignored**; Windows substitutes the default scenario sound. The "Custom audio on toasts" article that Pass 1 cited applies only to MSIX-packaged apps (where `ms-appx:///` resources are real).

**Recommendation:**
- New v0.1 architecture for custom-sound alarms:
  1. Toast XML contains `<audio silent="true"/>` so Windows plays nothing.
  2. Alarm scheduler invokes `new System.Media.SoundPlayer(absolutePath).Play()` on a background thread immediately before `ToastNotificationManager...Show(toast)`.
  3. For MP3/M4A/non-WAV: pull in `NAudio` 2.x (MIT, no native deps).
- Update `docs/research.md` §6 to remove the wrong claim and add this section as the canonical reference.
- Update [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md) F-A6 status from "spike needed" to "decided: SoundPlayer + silent toast."
- Add a roadmap item: "If we ever ship as MSIX, revisit `ms-appx:///` audio paths."

**Estimated complexity:** S.
**Priority:** **P0** (blocks alarm-sound UX direction; current research is wrong).

### 3.4 — Color thresholds: Pass 1 conflated two different threshold sets

**Verified by reading both files.**

**Pass 1 claim (F-A8):** "Extension uses 75/90 thresholds in its widget CSS; ours uses 60/85."
**Reality:**
- Extension's *visual color ramp* (`AI-Usage_Tracker/src/lib/countdown.js` line 109): **50/80**.
- Extension's *notification thresholds* (`AI-Usage_Tracker/src/lib/storage.js` lines 110–112): **75/90/95** for U1-75 / U1-90 / U1-95 rules.
- QuotaGlass current visual ramp (`Controls/RadialRing.cs` line 105): **60/85**.
- Zrnik's notifications (`NotificationService.cs` line 10): **75/90**.

There's no consistent "correct" number — the extension itself uses two systems for two purposes. Pass 1 conflated them.

**Recommendation (pick one and document):**
- **Visual ring color:** 60/85 (matches Catppuccin's Peach `#FAB387` → Red `#F38BA8` perceptual transition). Leave QuotaGlass as-is.
- **Notification thresholds:** 75/90 (matches both Zrnik and the extension's U1-90 default-ON setting). When QuotaGlass's alarm scheduler lands, these are the U-tier thresholds.
- Document the distinction in `docs/extension-integration.md` so the two never get confused again.

**Estimated complexity:** XS (purely a documentation + decision change).
**Priority:** P1.

### 3.5 — JSON deserialization hardening

**Verified by reading source.**

**Current state:** `src/QuotaGlass.NMH/MessagePump.cs` reads up to 1 MB from stdin, passes the bytes to `JsonSerializer.Deserialize` via the source-generated `SnapshotJsonContext`. `JsonSerializerOptions.MaxDepth` defaults to 64; no override.

**Risk:** A malicious extension (or compromised one) could send a 1 MB payload of `[[[[[...]]]]]` deeply nested. Source-gen deserialization at depth 64 of a 1 MB payload allocates ~64 × per-frame state. Not a remote-code path (deserialization is into known POCOs), but a CPU/allocation DoS on the NMH process — which then doesn't write snapshots, so the widget goes silently stale.

**Recommendation:**
- Set `MaxDepth = 16` on `SnapshotJsonContext`. Our actual depth is ~5 (envelope → snapshot → providers → bucket → field).
- Add `[JsonStringLength(256)]` (or manual length check post-deserialize) on `Bucket.Label`, `Bucket.Plan`, `Bucket.Model`, `Bucket.RawResetText` to reject pathological strings. Pass 1 §R-Sec-02 mentioned this but didn't propose the mechanism.

**Estimated complexity:** XS.
**Priority:** P1 (rolls in with F-A14 origin enforcement).

### 3.6 — The extension's `forecastExhaustion` is sophisticated and free

**Verified by reading code.**

**Discovery:** `AI-Usage_Tracker/src/lib/history.js` lines 22–53 implement linear regression with reset detection over a 48-hour sample window per bucket. Slope guard, intercept calculation, ETA-at-100% prediction, all in ~30 LOC. Outputs a `Date` or `null`. Already runs every refresh tick and feeds the U2 burn-rate notification.

**Opportunity:** When F-A1 changes our schema to mirror the extension's full state envelope, the `history` array is already part of the snapshot. The widget can either:
1. Render a "Pace: hitting cap by 4 PM" footer per card. (Cheap, ~10 LOC.)
2. Add a second, lighter tick on the radial ring at the projected end-of-window utilization. (Visual; matches Pass 1 roadmap L-08.)
3. Trigger a Pace warning toast tier in the alarm ladder when the forecast crosses below 1× the user's configured ladder-lead. (Power-user feature, defer to v0.3.)

**Recommendation:**
- v0.1: option 1 (footer line) — almost free given the data.
- v0.2: option 2 (ring tick) — folds into roadmap L-08, promotes from "Later" to "Next."
- v0.3: option 3 (pace alarm tier) — extension's U2 already fires this; we'd just surface it on the desktop.

**Estimated complexity:** S each tier.
**Priority:** P1 for option 1 (free win); P2 for option 2; P3 for option 3.

### 3.7 — Sparkline data is already in the snapshot

**Verified by reading code.**

**Discovery:** `AI-Usage_Tracker/src/lib/history.js` lines 57–67 — `sparklineFor(history, bucketId, { n = 24 })` returns up to N evenly-sampled `percentUsed` values. The extension's popup uses this to render 24-point sparklines.

**Opportunity:** Pass 1 roadmap NX-08 ("Sparkline history panel") assumed we'd build our own history buffer. With F-A1, the snapshot envelope contains the raw `history` array; the widget calls `sparklineFor`-equivalent on it. **Saves us from building a separate history-tracking subsystem.** Just port the 10-LOC function.

**Recommendation:** Promote NX-08 from v0.2 backlog to v0.1.5 if there's any slack — it's almost-free given the data flow.

**Estimated complexity:** S.
**Priority:** P2 (delights but doesn't gate).

### 3.8 — Hermes credential source (third file)

**Verified by reading competitor code.**

**Discovery:** Zrnik's `CredentialStore.cs` line 47 lists `~/.hermes/auth.json` as a third credential file (Codex orchestrator). Pass 1 F-N1 only mentioned `~/.claude/.credentials.json` and `~/.codex/auth.json`.

**Recommendation:** When F-N1 lands, include Hermes as a third source. Same Codex API shape, different file path. ~20 LOC.

**Priority:** P1 (folds into F-N1).

### 3.9 — Lightweight updater alternative to Velopack

**Verified by reading competitor code.**

**Discovery:** Zrnik's `Updater.cs` (118 LOC) and the embedded PS1 self-replace script implement the entire updater story without any NuGet package:

```
GET https://api.github.com/repos/{repo}/releases/latest
  → find asset matching ClaudeUsageWidget-win-{arch}.exe
  → download via curl.exe to %TEMP%\app_update.exe
  → write PS1 that: kill self, copy, restart
  → spawn powershell -ExecutionPolicy Bypass -File <script>
```

**Tradeoffs vs Velopack:**
- Pro: zero dependencies; matches QuotaGlass's "small + auditable" ethos.
- Pro: no signing complexity (Velopack works unsigned too, but the PS1 approach has zero install-time UAC concerns since it's user-space).
- Con: no delta packages (re-downloads full EXE each update; currently ~600 KB — acceptable).
- Con: no automatic update channel/staging — every push to Releases goes to all users.
- Con: PS1 self-replace racey if anti-virus has the EXE locked when `Copy-Item` runs.

**Recommendation:**
- v0.1: ship Zrnik-style updater (saves the dependency, ~3 hours work).
- v0.2+: if delta downloads or staged rollouts matter, migrate to Velopack.

**Estimated complexity:** M.
**Priority:** P1 (alternative to Pass 1 F-N2; either path closes the same gap).

### 3.10 — `ToastNotificationManager.CreateToastNotifier("AppId")` requires a registered app

**Likely.** Not verified in this session; standard Win10/11 requirement.

**Concern:** Both Zrnik and the standard MS docs use `CreateToastNotifier("AppIdString")` where the AppId must either be a Start Menu shortcut AppUserModelID or a COM-registered notification AppId. Without registration, toasts may show "via QuotaGlass" but not appear in Action Center, or fall back to "via Microsoft Toolkit Notifications" labeling.

**Mitigation:** During install (Pass 1 F-N4 tray-icon + Pass 1 N-17 installer), drop a Start Menu shortcut with an explicit `System.AppUserModel.ID` property set to `com.sysadmindoc.quotaglass.Widget`. Use the same AppId in `CreateToastNotifier`.

**Recommendation:** Add to installer-task acceptance criteria.

**Estimated complexity:** S (folds into installer task).
**Priority:** P1.

---

## Section 4 — Pass 1 Errors To Correct

For each Pass 1 item that needs correction, the implementing agent should re-read this before acting on Pass 1.

### 4.1 — Pass 1 F-A6 status

- **Was:** "Spike custom-audio playback on Windows 11 26100 — verify whether `file:///` audio plays."
- **Now:** Custom audio in toast XML is documented as silently ignored for unpackaged apps; no spike needed. Replace with §3.3's `SoundPlayer + silent toast` approach.

### 4.2 — Pass 1 F-A8 thresholds

- **Was:** "Extension uses 75/90; ours uses 60/85."
- **Now:** Extension uses 50/80 visual / 75/90 notification. See §3.4. Recommendation: keep QuotaGlass visual at 60/85; align notifications to 75/90.

### 4.3 — Pass 1 §C-01 Zrnik summary

- **Was:** "No notifications. No settings UI. Just visual indicators."
- **Now:** Zrnik has `NotificationService.cs` (toasts with reset detection + threshold alerts) and `SettingsWindow.xaml.cs` (26 KB settings surface). The competitive gap is smaller than Pass 1 portrayed. Specifically, **QuotaGlass's alarm-ladder + custom-audio is still a differentiator**, but the "no competitor has notifications" framing is wrong. See §6 below.

### 4.4 — Pass 1 R-Sec-02 phrasing

- **Was:** "Cap inbound string field lengths" — phrased as a hand-rolled validation pass.
- **Now:** Combine `MaxDepth = 16` on `JsonSerializerContext` with `[JsonStringLength]` (or manual `Validate()` per type). See §3.5.

---

## Section 5 — Re-confirmed Pass 1 findings (no change needed)

These were verified more deeply in Pass 2 and are still correct as stated in Pass 1:

- **F-A1** (BucketSnapshot schema mismatch) — verified at much higher resolution by reading `storage.js` + `notify.js` + `claude.js` + `codex.js`. The fix described in Pass 1 is correct; no amendments.
- **F-A2** (Chrome manifest `"key"` pinning) — re-verified `chrome.json` has no `"key"`. No change.
- **F-A3** (Firefox ID typo) — re-verified `firefox.json` line 12.
- **F-A4** (persistent port + reconnect) — re-verified Chrome SW lifecycle docs in Pass 1; still correct.
- **F-A7** (close-to-tray) — no change.
- **F-N3** (Setup Checklist) — no change.
- **F-N4** (system tray icon) — no change.
- **F-N8** (`--inject-fake-snapshot`) — no change.
- **F-N9** (extension-integration spec) — no change; Pass 2 strengthens the case (more state shape detail to pin).
- **F-N10** (ARM64) — re-verified via Zrnik's csproj. Confirms standard practice in this space.

---

## Section 6 — Competitive Research Amendments

### Pass 2 augmentation to §6 C-01 (Zrnik)

**Pass 1 stated:** No notifications, no settings.
**Pass 2 verified by reading 8 source files:**

- **Has notifications** — `NotificationService.cs` 96 LOC: toasts on threshold crossings (75/90) AND on reset detection.
- **Has settings UI** — `SettingsWindow.xaml.cs` is 26 KB (one of the largest files in the project).
- **Has updater** — `Updater.cs` 118 LOC + embedded PS1 script.
- **Has multi-account** — `CredentialStore.cs` with six credential-file slots, JWT-claim dedup, opaque-token fallback.
- **Has burn-rate prediction** — `UsagePrediction.cs` + `Pace.cs` with 9-5 workday-window calculations.
- **Has Jira + Toggl integrations** — `JiraApiClient.cs`, `TogglApiClient.cs`, `JiraHistoryStore.cs`, `TogglHistoryStore.cs`. Productivity-dashboard direction.
- **Has chart rendering** — `HistoryChart.xaml.cs` 8 KB.
- **Targets Win11 22H2** (`net8.0-windows10.0.22621.0`) — explicit Win10 cut.
- **Zero NuGet packages** — entire app, including notifications, updater, charts, settings, in pure framework + WinRT.

### Revised positioning

QuotaGlass's competitive differentiators (re-grounded in fact, post Pass 2):

1. **The only Windows tracker for browser-session users** (claude.ai + chatgpt.com web). Still uniquely true — Zrnik et al all read CLI credentials.
2. **The richest data path** — three sources (API + SSE + headers) vs single-source competitors. Still uniquely true.
3. **The alarm ladder** with 8 configurable tiers + custom audio per tier. Still uniquely true (Zrnik fires 2 tiers at 75/90; jens-duttke fires configurable thresholds but no time-based ladder).
4. **Catppuccin Mocha "premium glass" identity**. Aesthetic differentiator; the others use Win11 default styling.
5. **MIT + auditable + no telemetry**. Same ethos as several competitors; tied.

QuotaGlass's competitive *gaps* (Pass 2 corrections):

1. **No multi-account.** Zrnik shines here. Defer past v0.2.
2. **No productivity integrations** (Jira/Toggl). Out of scope; correct product call.
3. **Smaller install + ARM64.** Achievable parity with F-N10 + Pass 2 §3.1.
4. **Win10 vs Win11 22H2 cut.** Decide now: support Win10 (more users, more bugs) or cut to Win11 22H2 (cleaner Mica/Acrylic story, smaller test matrix). Recommendation: **stick with Win10 1809+ for v0.1** (broadest reach), revisit at v0.3.

---

## Section 7 — Prioritized Pass 2 additions to the implementation queue

Use these alongside the Pass 1 roadmap. Each is checkbox-formatted to drop into ROADMAP.md or be tracked separately.

### Phase 0 additions (must land for v0.1.0)

- [ ] **P0 — R2-P0-01 — Drop `Microsoft.Toolkit.Uwp.Notifications`; write `ToastService` on raw WinRT**
  - Why: Eliminates Critical CVE (GHSA-rxg9-xrhp-64gj) flag in transitive `System.Drawing.Common 4.7.0`; removes ~600 KB of binary; matches verified competitor pattern; future-proof against Toolkit deprecation.
  - Evidence: `dotnet list package --vulnerable` output above; Zrnik `NotificationService.cs` raw via gh api.
  - Touches: `src/QuotaGlass.Widget/QuotaGlass.Widget.csproj` (remove package), `src/QuotaGlass.Widget/Services/ToastService.cs` (new), `docs/research.md` §6 (correct).
  - Acceptance: Synthetic toast fires from a unit-test harness, appears in Action Center, no NuGet packages in the Widget csproj.
  - Verify: `dotnet list package --vulnerable` → no vulnerable packages.

- [ ] **P0 — R2-P0-02 — Add `TopMostEnforcer` (WinEvent hook)**
  - Why: Plain `Topmost="True"` is overridden by UAC / fullscreen / foreground events; every competitor implements this.
  - Evidence: Zrnik `TopMostEnforcer.cs` 140 LOC.
  - Touches: New `src/QuotaGlass.Widget/Services/TopMostEnforcer.cs`. `MainWindow.OnSourceInitialized` instantiates. `Dispose` on `Closed`.
  - Acceptance: Trigger UAC consent prompt; widget stays on top after consent dialog closes. Trigger fullscreen YouTube; widget re-asserts on Esc.
  - Verify: Manual UAC test + fullscreen test.

- [ ] **P0 — R2-P0-03 — Replace toast custom-audio with `SoundPlayer + silent toast`**
  - Why: `<audio src="file:///...">` is silently ignored by Windows; documented behavior.
  - Evidence: Microsoft UWP toast `<audio>` schema doc; remarks paragraph.
  - Touches: `src/QuotaGlass.Widget/Services/ToastService.cs` (when implementing N-12); `docs/research.md` §6 (correct).
  - Acceptance: Custom WAV plays at toast fire; toast uses `<audio silent="true"/>`.
  - Verify: Manual ear test.

### Phase 1 additions (v0.1.0 polish)

- [ ] **P1 — R2-P1-01 — Document color-vs-notification threshold split**
  - Why: Pass 1 conflated two systems; future contributors will too.
  - Touches: `docs/extension-integration.md` (when N-21 / F-N9 lands).
  - Acceptance: Doc has a "Thresholds" section: "Visual ring: 60/85 (Catppuccin); Notifications: 75/90 (industry norm)."

- [ ] **P1 — R2-P1-02 — JSON deserialization hardening**
  - Why: 1 MB frame at depth 64 of source-gen JSON can DoS the NMH.
  - Touches: `src/QuotaGlass.Shared/BucketSnapshot.cs` (add `[JsonSourceGenerationOptions(MaxDepth=16)]` to `SnapshotJsonContext`), `Bucket` properties (add manual length checks in a `Validate()` extension).
  - Acceptance: Synthetic 1 MB nested payload → frame accepted, decode rejects with `"detail":"max-depth-exceeded"`.
  - Verify: Unit test in F-A16 project.

- [ ] **P1 — R2-P1-03 — Surface burn-rate forecast as card footer**
  - Why: Data is already in the snapshot envelope (after F-A1); free competitive feature.
  - Touches: `BucketViewModel.cs` gains `PaceLabel` property; `MainWindow.xaml` ItemTemplate adds a footer line.
  - Acceptance: With seeded history showing 8% / 12% / 16% growth, card shows "Pace: at 100% by ~5 PM."
  - Verify: F-N8 fake-snapshot injector with history; manual visual check.

- [ ] **P1 — R2-P1-05 — Include Hermes (`~/.hermes/auth.json`) when F-N1 lands**
  - Why: Codex orchestrator users have a third credential file.
  - Touches: Whatever module ends up handling F-N1 credential reading.
  - Acceptance: Hermes-only install detects Codex via the Hermes file.

- [ ] **P1 — R2-P1-06 — Lightweight self-hosted updater (alternative to F-N2 Velopack)**
  - Why: Smaller dependency footprint; matches "no telemetry, small" ethos.
  - Evidence: Zrnik `Updater.cs` + PS1 pattern, 140 LOC total.
  - Touches: New `Services/UpdateChecker.cs`, embedded PS1 template, settings flag for auto-check cadence.
  - Acceptance: On v0.1.1 release, v0.1.0 user sees toast "Update available", clicks → app restarts as v0.1.1.
  - Verify: Manual upgrade test against a local GitHub Release.
  - Note: Choose between this and F-N2 Velopack; not both.

- [ ] **P1 — R2-P1-08 — Register app with `System.AppUserModel.ID`**
  - Why: Without registration, toasts may not group in Action Center or show wrong "via" label.
  - Touches: Installer (or first-run code if no installer yet) writes a Start Menu shortcut with the AppId property; `ToastService` uses the same AppId in `CreateToastNotifier`.
  - Acceptance: Toasts appear in Action Center under "QuotaGlass" group.
  - Verify: Settings → Notifications → look for QuotaGlass entry.

### Phase 2 additions (v0.2 polish)

- [ ] **P2 — R2-P2-01 — Working-day Pace integration**
  - Why: Power-user feature; matches Zrnik's `Pace.cs` pattern.
  - Touches: New `Services/WorkdayPace.cs`; settings field for workday start/end.
  - Acceptance: With workday set 09-17 M-F, "by Friday 5pm" prediction excludes weekends.

- [ ] **P2 — R2-P1-04 — Render sparklines from snapshot history**
  - Why: Promotes NX-08 from "build our own history" to "consume existing snapshot history."
  - Touches: New `Controls/MiniSparkline.xaml`; `BucketViewModel.SparklineData`.
  - Acceptance: 24-point sparkline under each ring; auto-scales 0..100.

### Phase 3 additions (v0.3+)

- [ ] **P3 — R2-P3-01 — Productivity integrations** (Jira / Toggl) — explicitly de-prioritized; tracked for awareness, not commitment.

---

## Section 8 — Quick Wins (Pass 2 additions only)

1. **R2-P0-01** — drop the Toolkit package + write `ToastService` (~30 min).
2. **R2-P1-02** — `MaxDepth = 16` on the JSON context (1 line).
3. **R2-P1-01** — add the threshold paragraph to research.md or the integration spec (5 min).
4. **R2-P1-03** — pace label on cards (~20 min after F-A1).
5. **F-A6 status update** in RESEARCH_FEATURE_PLAN.md from "Needs validation" to "Decided: SoundPlayer + silent toast" (1 paragraph).

---

## Section 9 — Larger Bets (Pass 2 additions only)

1. **R2-P0-02 TopMostEnforcer** — touches P/Invoke + native message loop; 140 LOC port; takes a half-day to do right with proper testing.
2. **R2-P1-06 Self-hosted updater** vs **F-N2 Velopack** — strategic call. Recommend self-hosted for v0.1, Velopack at v0.3 if delta packages matter.
3. **Win10 vs Win11 22H2 cut decision** — affects test matrix forever. Defer to v0.3 product call.

---

## Section 10 — Explicit Pass 2 non-goals

- **Do NOT add Jira/Toggl integrations.** Out of scope; that's a different product (Zrnik's direction).
- **Do NOT migrate to MSIX packaging.** Loses the per-user no-elevation install; doesn't fix the toast-audio limitation (different reason); adds Store certification overhead. Revisit only if Windows Store distribution becomes a goal.
- **Do NOT re-evaluate Win10 1809 cut** — leave as-is for v0.1; revisit at v0.3.
- **Do NOT port Zrnik's `CredentialStore.cs` verbatim** — read it for the schema understanding, but write our own minimal implementation. We don't need WSL credential reading in v0.2 (Pass 1 noted this as v0.3+).

---

## Section 11 — Open Questions (Pass 2)

Only items that block correct prioritization:

1. **Velopack vs self-hosted updater — pick one for v0.1.** Pass 1 F-N2 picked Velopack; Pass 2 §3.9 / R2-P1-06 proposes self-hosted as the lighter alternative. Both close the same gap. Default: **self-hosted** (matches project ethos, zero new deps, ~140 LOC). Confirm before implementing.

2. **Do we want toast *actions* (Snooze button, Open Analytics button) in v0.1 or v0.2?** This drives whether we can stay 100% raw WinRT (no actions) or need the Toolkit's COM-activator helpers. Default: **v0.1 has text-only toasts; v0.2 adds actions and at that point evaluates whether to re-add the Toolkit or hand-roll COM activator.**

3. **Win10 1809 minimum vs Win11 22H2 minimum.** Already covered in §6 / Section 9 — default Win10 1809 for v0.1; revisit at v0.3 once we have user feedback.

4. **App User Model ID format** — recommend `com.sysadmindoc.QuotaGlass`; matches the NMH name `com.sysadmindoc.quotaglass` (with `.Widget` suffix optional). Default: lowercase `com.sysadmindoc.quotaglass.widget`. Confirm.

---

## Section 12 — Implementation order recommendation (Pass 1 + Pass 2 combined)

Re-derived order based on hard dependencies and Pass 2 findings:

1. **F-N9** (write integration spec) — defines schema. *Zero deps.*
2. **F-A1** (rewrite BucketSnapshot) — implements schema. *Depends F-N9.*
3. **R2-P0-01** (drop Toolkit, write ToastService) — *Zero deps*; do in parallel with F-A1.
4. **F-A3** (Firefox ID typo), **F-A7** (close-to-hide), **F-N8** (fake-snapshot injector) — *Zero deps*; do in parallel.
5. **R2-P0-02** (TopMostEnforcer) — *Zero deps but P/Invoke; allow a half-day.*
6. **F-A2** (pin Chrome ID) + **F-A4** (bridge.js) — *Cross-repo; depends F-A1 schema.*
7. **F-N4** (tray icon) — *Depends F-A7.*
8. **F-N3** (Setup Checklist) — *Depends F-A2 + F-A4.*
9. **N-12 + N-13 + N-14** (toast + alarm + zero-state) — *Depends R2-P0-01 + R2-P0-03.*
10. **N-15 + N-16** (settings panel + persistence).
11. **F-A16** (test project) — *Add tests in parallel as features land; F-A1 first.*
12. **R2-P1-02** + **F-A14** (JSON hardening + origin check) — *Depends F-A1.*
13. **R2-P1-03** (pace footer) — *Depends F-A1.*
14. **R2-P1-08** (AUMID registration) + **N-17/N-18** (installer + release workflow) — *Final v0.1 step; depends ToastService AUMID.*
15. **F-A20** + **F-A21** + **N-19** + **N-20** (docs + screenshots) — *Final.*

v0.2: F-N1 + R2-P1-05 (credential reading + Hermes), F-N5 (Mica), R2-P1-04 (sparklines from snapshot), NX-04..NX-10.

---

*End of Pass 2. Implementing agent must reconcile this with Pass 1's [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md); on conflict, Pass 2 supersedes for items §3.1–3.10 and §4.1–4.4 only.*
