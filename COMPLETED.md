# QuotaGlass Completed Work

This file summarizes shipped project state. Release-level detail remains in `CHANGELOG.md`.

## Product Baseline

- Windows desktop companion for `AI-Usage_Tracker`.
- .NET 9 solution with shared schema/persistence, native messaging host, WPF widget, and xUnit tests.
- Floating always-on-top WPF widget with radial quota cards, tray integration, settings, calendar/log panels, themes, and premium UI polish.
- Native messaging host receives browser-extension snapshots, writes local JSON snapshots atomically, and registers per-user browser host manifests.
- Widget watches snapshots through file-system and named-pipe paths, merges extension and local-credential producers, and preserves history/multi-account schema fields.

## Shipped Runtime Features

- Browser bridge integration contract and native host registration for Chrome, Edge, Chromium, and Firefox.
- Schema-versioned snapshot model with multi-provider and multi-account support.
- Alarm ladder, zero-state alerts, anomaly/spike alerts, custom sound playback, snooze/open toast actions, and Focus Assist suppression.
- Settings persistence for alarms, display thresholds, themes, autostart, webhooks, and setup-card state.
- Direct credential polling fallback for Claude Code, Codex, and Hermes credential files, with local snapshot output.
- Tray controls, close-to-tray behavior, refresh/reset actions, setup card, embedded logs, stale-state escalation, and self-update checks.
- Diagnostics bundle generation with recursive redaction for snapshots, settings, logs, and local credential snapshot data.
- Release workflow, installer, portable publish paths, x64/arm64 targets, and self-replace update script hardening.

## Quality and Verification Baseline

- Last documented local verification: `dotnet test QuotaGlass.sln --no-restore` with 101 passing tests.
- Test coverage includes shared persistence/schema, NMH credential and diagnostics paths, widget snapshot merging, alarm scheduling, updater guards, toast action parsing, and view-model regressions.
- Package vulnerability checks were documented as clean in the latest research/pass notes.

## Documentation Consolidation

- Root planning is consolidated into `ROADMAP.md`, `COMPLETED.md`, and `RESEARCH_REPORT.md`.
- Historical research passes were archived under `docs/archive/research/`.
- Active integration specs remain in `docs/extension-integration.md` and `docs/bridge-integration.md`.
