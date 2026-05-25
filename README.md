# QuotaGlass

[![Version](https://img.shields.io/badge/version-0.1.0-blue.svg)](https://github.com/SysAdminDoc/QuotaGlass/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-0078D6.svg)](#install)
[![Stack](https://img.shields.io/badge/.NET-9.0-512BD4.svg)](#build-from-source)

> **Always-visible Claude + Codex usage on your Windows desktop.** Draggable glass widget, radial-ring countdowns, custom-sound notifications, and a configurable alarm ladder (24 h / 12 h / 6 h / 3 h / 1 h / 30 m / 15 m / 5 m) so you know the moment your weekly quota renews.

QuotaGlass is the **desktop companion** to the [AI-Usage_Tracker](https://github.com/SysAdminDoc/AI-Usage_Tracker) browser extension. The extension already handles the authenticated API path against `claude.ai` and `chatgpt.com`. QuotaGlass surfaces that data on your desktop as a floating widget with OS-native toasts, so you don't have to keep a tab open or pin the popup.

## Features (v0.1.0)

- **Floating glass widget** — borderless, always-on-top, draggable, snaps to screen edges. Catppuccin Mocha by default, no pill backdrops, 8–12 px corner radii.
- **Per-provider radial-ring countdowns** — one card per Claude / Codex bucket, percent-used in the ring, time-to-reset in the center.
- **OS toast notifications with custom sound** — drop in any `.wav`, `.mp3`, `.m4a`, `.aac`, or `.wma` (WAV plays via `SoundPlayer`; everything else via WPF `MediaPlayer` / Media Foundation). Fires at user-configurable thresholds.
- **Reset alarm ladder** — pop a toast at 24 h, 12 h, 6 h, 3 h, 1 h, 30 m, 15 m, 5 m, and at-reset. Each tier independently toggleable.
- **Zero-state alert** — special toast when a bucket hits 0% remaining (you've burned the whole window).
- **Live data via native messaging** — the extension pipes snapshots into QuotaGlass's local daemon over Chrome's `chrome.runtime.connectNative` API; no second auth surface.
- **Snapshot persistence** — last-known usage cached to `%LOCALAPPDATA%\QuotaGlass\snapshot.json`, so the widget shows something useful even when Chrome is closed.
- **Settings panel** — embedded, in-widget, async; no separate window. Refresh interval, alarm ladder, custom sound, theme.
- **Embedded log panel** — collapsed by default, surfaces NMH connection state and last fetch error if any.

## How it works

```
┌────────────────────────────────────────────────────────────────┐
│  Chrome / Edge / Firefox                                       │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ AI-Usage_Tracker extension (existing)                    │  │
│  │  • Polls Claude /api/organizations/{org}/usage           │  │
│  │  • Polls Codex /backend-api/wham/usage                   │  │
│  │  • Captures SSE message_limit + ratelimit headers        │  │
│  │  • connectNative("com.sysadmindoc.quotaglass")           │  │
│  └────────────────────────┬─────────────────────────────────┘  │
└───────────────────────────┼────────────────────────────────────┘
                            │  stdin/stdout, 4-byte LE length
                            │  prefix + UTF-8 JSON
                            ▼
              ┌───────────────────────────────┐
              │  QuotaGlass.NMH.exe           │
              │  (.NET 9 console, ~12 KB)     │
              │  • reads bucket snapshots     │
              │  • atomically writes to       │
              │    %LOCALAPPDATA%\QuotaGlass  │
              │    \snapshot.json             │
              └────────────────┬──────────────┘
                               │  FileSystemWatcher
                               ▼
              ┌───────────────────────────────┐
              │  QuotaGlass.Widget.exe        │
              │  (.NET 9 WPF, always-on-top)  │
              │  • radial-ring countdowns     │
              │  • toast notifications        │
              │  • alarm-ladder scheduler     │
              └───────────────────────────────┘
```

## Install

> **Status: pre-release.** No installer is published yet. The instructions below describe the v0.1.0 path; see [ROADMAP.md](ROADMAP.md) Phase 1 Batch 8 for shipping status. Build-from-source works today (see next section).

When v0.1.0 ships:

1. Install the [AI-Usage_Tracker extension](https://github.com/SysAdminDoc/AI-Usage_Tracker) (Chrome / Edge / Brave / Firefox). The extension version that adds native-messaging support will be ≥ 0.2.0.
2. Download `QuotaGlass-Setup-vX.Y.Z.exe` from the [Releases page](https://github.com/SysAdminDoc/QuotaGlass/releases/latest).
3. Run the installer. Windows SmartScreen will prompt because v0.1.x binaries are unsigned — click **More info** → **Run anyway**. The installer:
   - Places the binaries in `%LOCALAPPDATA%\Programs\QuotaGlass\`.
   - Runs `QuotaGlass.NMH.exe --register` (writes HKCU keys for Chrome, Edge, Chromium, Firefox under `Software\*\NativeMessagingHosts\com.sysadmindoc.quotaglass`).
   - Drops a Start Menu shortcut with `System.AppUserModel.ID = com.sysadmindoc.QuotaGlass.Widget` (required for toast grouping in Action Center).
   - Adds an HKCU `Run` entry to autostart the widget on login (toggleable in Settings).
4. Reload the AI-Usage_Tracker extension in `chrome://extensions/`.
5. The widget launches automatically and pops a "QuotaGlass is in your tray" toast. Right-click the tray icon → **Show widget**.

## Build from source

```bash
cd ~/repos/QuotaGlass
dotnet build QuotaGlass.sln -c Release
dotnet publish src/QuotaGlass.Widget/QuotaGlass.Widget.csproj -c Release -r win-x64 --self-contained false
dotnet publish src/QuotaGlass.NMH/QuotaGlass.NMH.csproj    -c Release -r win-x64 --self-contained false
```

For ARM64 (Surface Pro X / Snapdragon-X laptops), swap `win-x64` → `win-arm64`.

Requires .NET 9 SDK (`winget install Microsoft.DotNet.SDK.9`).

To register the native messaging host against a local build:

```bash
./publish/QuotaGlass.NMH.exe --register
```

To wipe local state during dev:

```bash
./publish/QuotaGlass.NMH.exe --purge
```

To exercise the widget in isolation (without the extension/NMH chain):

```bash
dotnet run --project src/QuotaGlass.Widget -- --inject-fake-snapshot
```

## Run tests

```bash
dotnet test QuotaGlass.sln -c Release
```

Covers atomic-write round-trip, schema versioning, extension-payload deserialization fidelity, JSON depth-bomb rejection, and unknown-field tolerance.

## OSS landscape & why this exists

See [docs/research.md](docs/research.md) for the full survey. TL;DR:

| Option | Why it's not the fit |
|---|---|
| Rainmeter + HttpRequestPlugin | Skin DSL is awkward for stateful logic (alarm scheduler, snooze, fire-once). Re-solves auth from scratch. Loses the burn-rate + 30-day history work already in `AI-Usage_Tracker`. |
| Tauri / Electron port of the extension | Duplicates the JS data layer in a second runtime. ~150 MB install for a 32 px badge. Roadmap UC-01 in the extension explicitly parks this. |
| Direct Chromium cookie reads (DPAPI) | Chromium changed encryption mid-2024 + ongoing churn. Maintenance treadmill. |
| Standalone WPF widget polling the APIs directly | Means re-implementing Claude org discovery, SSE interception, and WHAM token handling. The extension already does it correctly. |
| **Native messaging bridge + WPF widget (this project)** | Single source of truth in the extension; widget is a thin display + notification surface; no second auth surface to maintain. |

## Privacy

- Nothing leaves your machine. No analytics, no telemetry, no remote servers.
- The native messaging channel is process-local stdin/stdout — it cannot be sniffed by other apps.
- Snapshots are written to your local `%LOCALAPPDATA%` folder. Delete the folder, delete the history.
- Source is fully auditable; no obfuscation, no minification.

## License

[MIT](LICENSE)
