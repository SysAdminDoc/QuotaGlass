# QuotaGlass — Research Dossier

**Last updated:** 2026-05-24

This document captures the research that informed the project's architecture choices. It is the canonical "why" behind QuotaGlass — every decision in [ROADMAP.md](../ROADMAP.md) cites a finding here.

---

## 1. Problem statement

The user runs out of Claude / Codex quota without warning. Both providers throttle on a mix of 5-hour and weekly windows, and the renewal time lives on a settings page you have to navigate to. The brief:

- Always-visible Windows desktop widget showing current usage for Claude + Codex.
- Configurable refresh interval (default 30 min).
- Custom-sound notification when usage hits 0% remaining.
- Visible reset countdown.
- Ladder of pop-up reminders before reset: 24 h / 12 h / 6 h / 3 h / 1 h / 30 m / 15 m.

Two pre-existing projects supply pieces of the solution:

- **[AI-Usage_Tracker](https://github.com/SysAdminDoc/AI-Usage_Tracker)** — browser extension (Chrome MV3 + Firefox MV3 + Tampermonkey userscript). Already polls the Claude `/api/organizations/{org}/usage` and Codex `/backend-api/wham/usage` endpoints, intercepts Claude SSE `message_limit` events, captures `anthropic-ratelimit-unified-*` response headers, fires five notification rule types (R1 renewal-imminent, R2 renewal-arrived, U1 threshold, U2 burn-rate forecast, D1 daily briefing), and maintains a 30-day rolling history.
- **[Claude-Ultimate-Enhancer](https://github.com/SysAdminDoc/Claude-Ultimate-Enhancer)** (CUE) — Claude.ai userscript with theme engine, usage monitor, prompt library. Shadow DOM isolation pattern documented here is useful but not directly applicable to a native widget.

---

## 2. OSS Windows widget landscape (May 2026)

| Tool | License | Stack | Custom-logic surface | Fit for QuotaGlass |
|---|---|---|---|---|
| **Rainmeter** | GPL-2.0 (core) + CC for skins | C++ core, Lua + INI skins, C++/C# plugins | Skin DSL + plugin SDK | Strong for static dashboards. Skin DSL is awkward for stateful logic (alarm scheduler, snooze, fire-once idempotency keys). Would require a C# plugin anyway, at which point the WPF widget is cheaper. |
| **Seelen UI** | LGPL-3.0 | Rust + Tauri + React | Full desktop overhaul (taskbar, dock, tiling WM) | Out of scope — replaces the shell, not a widget host. |
| **JaxCore** | MIT | Rainmeter skins | Same as Rainmeter | Inherits Rainmeter's tradeoffs. |
| **Wigify** | MIT | HTML/CSS/JS in Electron-style shell | DOM | Same architecture as our extension; offers no advantage over just shipping a desktop browser PWA. |
| **Eww (Elkowars Wacky Widgets)** | MIT | Rust + ELisp-style config | Limited | Linux/X11 + Wayland focused. No Windows target. |
| **Windhawk** | GPL-3.0 | C++ DLL "mods" injected into system processes | C++ mod SDK | Wrong tool — modifies Windows shell behavior, not a widget host. |
| **Uwidgets** | Proprietary (free) | C#/WPF or WinUI | Closed | Closed-source, can't bundle our logic into it. |
| **BeWidgets** (Microsoft Store) | Proprietary | UWP | None | No custom-widget API. |
| **Conky** | GPL-3.0 | C + Lua | Lua config | Linux only. |
| **xbar / SwiftBar / Übersicht** | MIT/MIT/MIT | macOS menubar / WebKit | Shell scripts / HTML | macOS only. |
| **Direct WPF / WinUI 3 widget** | n/a | C# .NET 9 | Full | The "build it ourselves" path. Bypasses the framework tax. |

**Verdict:** **Build a direct WPF widget.** None of the existing widget frameworks meaningfully shorten the path. Rainmeter is the only mature contender, but a Rainmeter skin still needs a C# plugin for the stateful alarm logic; once you've written that plugin, you've also paid the WPF surface cost minus the rendering polish. Direct WPF gives full control over the Catppuccin Mocha glassmorphism, draggable behavior, and toast integration with zero framework lock-in.

---

## 3. Data-source options (how does the widget get the usage data?)

This is the critical architecture decision. Claude and Codex authentication lives in the browser session — neither provider has a published "give me my usage" endpoint that accepts a long-lived token from a desktop app.

### Option A — Re-implement scraping in the widget

Embed WebView2, log the user in, scrape DOM or call the same `/api/organizations/{org}/usage` and `/backend-api/wham/usage` endpoints from inside the embedded browser.

- **Pro:** No browser-extension dependency.
- **Con:** Forces the user to log in twice (browser + widget). Embedded WebView2 chrome looks awful as a widget. Duplicates the API path that AI-Usage_Tracker already maintains. Re-fights every Claude / OpenAI hydration race the extension already solved.
- **Verdict:** Pass.

### Option B — Read Chromium cookies directly from disk (cfranci pattern)

`%LOCALAPPDATA%\Google\Chrome\User Data\Default\Network\Cookies` is a SQLite file. The auth cookie is DPAPI-encrypted on Windows. Once decrypted, the widget can call the JSON APIs directly.

- **Pro:** No extension dependency. Works even when Chrome is closed (cached cookies still valid).
- **Con:** Chromium [changed cookie encryption mid-2024](https://chromium.googlesource.com/chromium/src/+/main/components/os_crypt/sync/os_crypt_win.cc) (app-bound encryption via `IElevator` COM). Maintenance treadmill. Each Chrome major bump risks breaking the decrypt path. Doesn't cover Firefox (different DB, different encryption).
- **Verdict:** Pass as the primary; reconsider as a fallback in v0.4+.

### Option C — Native messaging from the extension

Chrome / Edge / Firefox all support [Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging): an extension can `connectNative("com.foo.bar")` to a registered local executable, communicating via stdin/stdout with a 4-byte little-endian length prefix + UTF-8 JSON payload (max 1 MB extension→host, 4 GB host→extension).

- **Pro:** Extension stays the single source of truth for auth. Widget is a thin display surface. Zero duplication.
- **Con:** The native messaging port only opens when the extension's service worker is alive (Chrome MV3 service workers die after 30 seconds idle). Widget shows stale data when Chrome is fully closed.
- **Mitigation:** Snapshot persisted to `%LOCALAPPDATA%`. Widget shows "Last fetched 12 min ago" honestly. The user is statistically *very likely* to have Chrome open during waking hours.
- **Verdict:** **Picked.** This is also AI-Usage_Tracker roadmap item L-12.

### Option D — User pastes an API key (Anthropic Admin / OpenAI)

The widget calls `api.anthropic.com/v1/organizations/usage_report/claude_code` and OpenAI's `/v1/usage` directly, server-to-server, no browser involved.

- **Pro:** Always works regardless of browser state. No native-messaging hook needed.
- **Con:** Anthropic Admin keys are workspace-scoped, not user-scoped — they give you billing-level data, not the per-window quota the user actually wants. OpenAI's usage API gives token counts but not the 5-hour reset window.
- **Verdict:** Pass for v0.1; revisit as supplementary data source in v0.4+ (matches AI-Usage_Tracker roadmap items NX-01 / NX-02).

### Final architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Extension (AI-Usage_Tracker, modified)                          │
│   bridge.js — on each successful refresh, forward bucket        │
│              snapshot to NMH via runtime.sendNativeMessage()    │
└──────────────────────────┬──────────────────────────────────────┘
                           │  4-byte LE length + UTF-8 JSON
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│ QuotaGlass.NMH.exe (.NET 9 console)                             │
│   • Stdin reader loop on background thread                      │
│   • Validates origin (chrome-extension://<allowed ID>)          │
│   • Decodes JSON into BucketSnapshot                            │
│   • Atomic-writes %LOCALAPPDATA%\QuotaGlass\snapshot.json       │
│   • (Future) Forwards to running Widget via named pipe          │
└──────────────────────────┬──────────────────────────────────────┘
                           │  FileSystemWatcher
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│ QuotaGlass.Widget.exe (.NET 9 WPF)                              │
│   • Reads snapshot on launch + on file-change                   │
│   • Renders one card per provider × bucket                      │
│   • Schedules alarm-ladder toasts via System.Threading.Timer    │
│   • Toast XML with custom <audio src="..."/>                    │
└─────────────────────────────────────────────────────────────────┘
```

---

## 4. Library picks

| Concern | Pick | Why |
|---|---|---|
| Native messaging host framing | **[acandylevey/NativeMessaging](https://github.com/acandylevey/NativeMessaging)** (MIT, C#) | Handles 4-byte little-endian length-prefix framing, `--register`/`--unregister` against HKCU, allowed-origin validation. Saves a day of stdin/stdout binary plumbing. May vendor a slim subset if dependency footprint becomes a concern. |
| WPF dark theme | **Hand-rolled Catppuccin Mocha ResourceDictionary** | The user's house style; matches existing portfolio (Snapture, Images, TeamStation). No external theme library needed. |
| Radial-ring drawing | **Native WPF `Path` + `ArcSegment`** | One ~40-line behavior class. No SkiaSharp dep, no third-party charting library. |
| Toast notifications | **`Microsoft.Toolkit.Uwp.Notifications`** (now `CommunityToolkit.WinUI.Notifications` in .NET 9 / 10) | Official Microsoft library. `ToastContentBuilder` fluent API. Supports `<audio src="file:///..."/>` for custom WAV. |
| Snapshot serialization | **`System.Text.Json` + source-generated `JsonSerializerContext`** | AOT-friendly. Avoids reflection on the hot path. |
| Atomic file write | **`File.WriteAllText` to `.tmp` + `File.Move(replace: true)`** | Standard pattern, no library needed. |
| Settings persistence | **`%LOCALAPPDATA%\QuotaGlass\settings.json`** + same atomic write | Mirrors snapshot.json discipline. |
| Logging | **`Microsoft.Extensions.Logging` + file provider** | Plumbing-grade. Log to `%LOCALAPPDATA%\QuotaGlass\logs\quotaglass-{date}.log`, surface tail in widget log panel. |

**No** packages used for: MVVM (raw `INotifyPropertyChanged`), DI (manual), reactive (raw events). The widget is small enough that adding a framework would cost more than the convenience.

---

## 5. Competitive matrix — what's already out there?

Pulled from the AI-Usage_Tracker ROADMAP appendix (sources [#1]..[#33]). Filtered to *desktop-surface* projects only — pure browser extensions are excluded since QuotaGlass is the desktop layer for those.

| Project | Stack | Providers | Key features | What we take | What we drop |
|---|---|---|---|---|---|
| **[Tokens 4 Breakfast](https://www.tokens4breakfast.app/)** | macOS menubar, Swift | Claude Web, Claude Code, OpenAI, Copilot, Cursor, OpenRouter, DeepSeek, Mistral | Focus Mode $-caps, 30-day run rate, month-end bill prediction, plan optimization recs | Multi-provider mindset (long term) | macOS-only, $7.99 paid; we're MIT |
| **[hamed-elfayome/Claude-Usage-Tracker](https://github.com/hamed-elfayome/Claude-Usage-Tracker)** | macOS menubar, SwiftUI | Claude | Unlimited profiles, 5 icon styles, 13 languages, peak-hours indicator, embedded sign-in browser | Pace marker on ring (v0.3) | macOS-only |
| **[steipete/CodexBar](https://github.com/steipete/CodexBar)** | macOS menubar, Swift | 29 providers | Device-flow + browser-cookie + OAuth + API-key auth ladder, 15+ themes, keybinding CLI dashboard | Provider plugin architecture (v0.5+) | macOS-only |
| **[Maciek-roboblog/Claude-Code-Usage-Monitor](https://github.com/Maciek-roboblog/Claude-Code-Usage-Monitor)** | Python terminal | Claude Code | P90 ML prediction, WCAG-contrast TUI, plan auto-detect (Pro/Max5/Max20), Sentry opt-in | Plan auto-detect heuristic (v0.4) | TUI; we're GUI |
| **[long-910/vscode-claude-status](https://github.com/long-910/vscode-claude-status)** | VSCode extension | Claude Code | Status-bar token+cost | Demonstrates editor-surface demand | Different surface (editor vs desktop) |
| **[ryoppippi/ccusage](https://github.com/ryoppippi/ccusage)** | Node CLI | Claude Code | Reads local `~/.claude/projects/*.jsonl`; daily/monthly/5-hour blocks | Sister-project potential | CLI; complementary not competitive |

**Gap QuotaGlass fills:** Every existing desktop surface is **macOS** or **terminal**. There is no Windows-native floating widget for Claude + Codex usage as of May 2026. This is the only Windows tracker that's *not* a browser extension or a CLI.

---

## 6. Notification mechanism research

### Windows toast XML with custom audio

```xml
<toast>
  <visual>
    <binding template="ToastGeneric">
      <text>Claude weekly resets in 1 hour</text>
      <text>Current usage: 87 % of weekly window</text>
    </binding>
  </visual>
  <audio src="ms-appx:///Sounds/reset.wav" loop="false"/>
  <actions>
    <action content="Open dashboard" arguments="action=open-dashboard"/>
    <action content="Snooze 30 m" arguments="action=snooze&amp;mins=30"/>
  </actions>
</toast>
```

- `audio src=` accepts `ms-appx:///` (packaged), `ms-appdata:///local/...` (per-user `%LOCALAPPDATA%`), or `file:///` (absolute path).
- For unpackaged WPF apps using `Microsoft.Toolkit.Uwp.Notifications`, **`file:///` is the only reliable scheme**. The toast handler resolves the path before raising the notification.
- The toast appears in Action Center even if dismissed, so missed alarms aren't lost.

### Fire-once idempotency

Each scheduled alarm gets a stable key: `<provider>-<bucket>-<tier>-<resetISO>`. The widget persists fired keys to `settings.json` and refuses to re-fire the same key. This matches the pattern AI-Usage_Tracker already uses for R1/R2 notifications.

### Custom sound user-experience

User picks a `.wav` / `.mp3` / `.m4a` via Win32 OpenFileDialog. File is copied (not referenced) into `%LOCALAPPDATA%\QuotaGlass\sounds\` so deletions/moves at the source don't break the alarm. Toast XML references the local path. WAV/MP3 are both supported by the Windows toast XML audio renderer on Windows 10 build 1607+; MP3 needs the `audio src=` extension to match.

---

## 7. Extension changes required

Two-line PR in `AI-Usage_Tracker`:

1. Manifest: add `"nativeMessaging"` to `permissions` (Chrome MV3 + Firefox MV3 both honor it).
2. New `src/lib/bridge.js`: on each successful refresh tick (existing event), call:

```js
const port = chrome.runtime.connectNative("com.sysadmindoc.quotaglass");
port.postMessage({ kind: "snapshot", buckets: latestBuckets, ts: Date.now() });
port.disconnect();
```

Plus the corresponding `browser.runtime.connectNative` for the Firefox manifest. The userscript build is unaffected (Tampermonkey doesn't expose native messaging — userscript users keep the existing in-page notification path).

A registered Native Messaging Host on Windows requires:

- A manifest JSON file at any absolute path:

```json
{
  "name": "com.sysadmindoc.quotaglass",
  "description": "QuotaGlass desktop bridge",
  "path": "C:\\Users\\<user>\\AppData\\Local\\Programs\\QuotaGlass\\QuotaGlass.NMH.exe",
  "type": "stdio",
  "allowed_origins": [
    "chrome-extension://<AI-Usage_Tracker-extension-id>/"
  ]
}
```

- A registry key at `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.sysadmindoc.quotaglass` whose `(Default)` value is the absolute path to the manifest JSON.
- For Edge: `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.sysadmindoc.quotaglass`.
- For Firefox: `HKCU\Software\Mozilla\NativeMessagingHosts\com.sysadmindoc.quotaglass` plus `allowed_extensions: ["aiusagetracker@sysadmindoc"]` in the manifest instead of `allowed_origins`.

`QuotaGlass.NMH.exe --register` writes all three when the corresponding browser is installed (detected by the presence of its `User Data` folder or registry root).

---

## 8. Risks & mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| Extension service worker dies, snapshot grows stale | High | "Last fetched X min ago" surfaced honestly in widget. Toast tier "stale data" fires if no refresh > 2 × interval. |
| User has only userscript installed (no extension API) | Medium | Document that QuotaGlass requires the extension build, not userscript. Settings page detects this and shows install link. |
| Chrome / Edge bump breaks native messaging registry path | Low | Browser vendors keep this path stable for years; the last Chrome breakage was in 2018. |
| Toast audio not playing on Windows 11 | Medium | Fallback: `System.Media.SoundPlayer` for WAV, `NAudio` later if MP3 toast audio proves unreliable on a given Win11 build. |
| User wants Linux / macOS support | Low | Out of v0.1 scope. Architecture-compatible: `QuotaGlass.NMH` is portable .NET; only `QuotaGlass.Widget` is WPF-bound. Replace widget with Avalonia in a future Linux/macOS port. |
| Conflict with existing AI-Usage_Tracker browser notifications | Medium | Settings page in extension gets a "Use QuotaGlass for notifications" master toggle that suppresses in-browser notifications when on. |

---

## 9. Decisions captured (so they don't come back)

- **MIT license** — matches AI-Usage_Tracker, simplifies downstream packaging.
- **PUBLIC GitHub repo** — `SysAdminDoc/QuotaGlass`. Not X-ray related; the "X-ray repos must be PRIVATE" rule does not apply.
- **No pill / oval / fully-rounded backdrops** — global hard rule. All backdrops use 4–12 px corner radii.
- **No telemetry** — matches AI-Usage_Tracker's "nothing leaves your browser" promise.
- **No paid tier** — open-source, no freemium gate.
- **Catppuccin Mocha dark default** — matches the user's house style; a Latte light variant ships in v0.2.

---

## 10. Sources

- [Chrome Native Messaging guide](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging)
- [MDN — Native messaging in WebExtensions](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Native_messaging)
- [acandylevey/NativeMessaging — C# library](https://github.com/acandylevey/NativeMessaging)
- [Rainmeter Plugin SDK](https://docs.rainmeter.net/developers/)
- [ilsasdo/rainmeter-httprequest](https://github.com/ilsasdo/rainmeter-httprequest)
- [Rainmeter alternatives roundup (AlternativeTo, 2026)](https://alternativeto.net/software/rainmeter/)
- [XDA — Open-source Windows customization tools](https://www.xda-developers.com/open-source-tool-for-windows-desktop/)
- [Microsoft Docs — Send a local toast notification from a C# app](https://learn.microsoft.com/en-us/windows/apps/design/shell/tiles-and-notifications/send-local-toast)
- [Microsoft Docs — Custom audio on toasts](https://learn.microsoft.com/en-us/windows/apps/design/shell/tiles-and-notifications/custom-audio-on-toasts)
- AI-Usage_Tracker ROADMAP.md (sources #1..#33) — full competitive landscape
