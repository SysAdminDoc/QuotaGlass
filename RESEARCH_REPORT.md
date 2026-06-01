# QuotaGlass Research Report

This report summarizes the current research state. Historical pass-by-pass evidence is archived under `docs/archive/research/`.

## Product Positioning

QuotaGlass exists because the browser extension already owns the authenticated Claude/Codex usage path, while Windows users need an ambient desktop surface for quota state. The best product shape remains:

- AI-Usage_Tracker as the browser data collector.
- QuotaGlass.NMH as the local native messaging bridge and optional credential poller.
- QuotaGlass.Widget as the desktop display, alarm, toast, tray, and settings surface.

## Competitive Findings

- Existing Windows-native Claude usage widgets mostly read Claude Code credential files.
- QuotaGlass's stronger differentiator is browser-session visibility for claude.ai and chatgpt.com Codex users, plus the alarm ladder/custom-audio desktop surface.
- Rainmeter skins are too weak for stateful alarms, snooze, setup, diagnostics, and multi-source merging.
- Electron/Tauri would duplicate the extension runtime and add install weight for a small desktop companion.
- Direct Chromium cookie reads remain a maintenance risk because browser storage and encryption behavior changes frequently.

## Architectural Decisions

- Keep QuotaGlass native Windows/.NET for WPF, tray, toast, registry, and scheduled-task integration.
- Keep the extension as the source of truth for browser-authenticated usage data.
- Use local-only storage under `%LOCALAPPDATA%\QuotaGlass`.
- Avoid telemetry and remote servers.
- Avoid `Microsoft.Toolkit.Uwp.Notifications`; raw Windows notifications avoid the vulnerable transitive dependency and are enough for the current feature set.
- Keep the installer/self-updater GitHub Releases based instead of moving to MSIX.
- Keep Avalonia, WinUI 3, Tauri, and Rainmeter as deferred pivots rather than active work.

## Open Research / Runtime Validation

- Verify the Claude Code OAuth refresh endpoint with a real Claude Code credential environment.
- Capture representative screenshots from a real extension/runtime setup.
- Validate CI behavior through an actual PR workflow rather than direct local push.
- Revisit provider plugin contracts only when a real second-provider use case appears.

## Archived Evidence

- `docs/archive/research/RESEARCH_FEATURE_PLAN.md`
- `docs/archive/research/RESEARCH_PASS_2.md`
- `docs/archive/research/RESEARCH_PASS_3.md`
- `docs/archive/research/RESEARCH_PASS_4.md`
- `docs/archive/research/RESEARCH_PASS_5.md`
- `docs/archive/research/RESEARCH_PASS_6.md`
- `docs/archive/research/RESEARCH_PASS_7.md`
- `docs/archive/research/research.md`
