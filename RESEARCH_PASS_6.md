# Project Research and Hardening Pass 6

**Date:** 2026-05-25  
**Scope:** Production-readiness audit after v0.9 fast-follow work. This pass re-read the runtime paths most likely to fail in production: diagnostics, snapshot merging, local credential snapshots, alarm scheduling, toast actions, updater, scheduled-task registration, setup-card state, settings reset, stale-state UI, tray wiring, README, and release workflow.

## Evidence Reviewed

- NMH entry/runtime: `Program.cs`, `MessagePump.cs`, `CredentialPoller.cs`, `Diagnostics.cs`, `HostRegistrar.cs`, `ScheduledTaskRegistration.cs`, `SnapshotPipeServer.cs`.
- Shared persistence/schema: `AppPaths.cs`, `AtomicJsonFile.cs`, `HistoryStore.cs`, `FiredRulesStore.cs`, `BucketSnapshot.cs`.
- Widget runtime/UI state: `SnapshotWatcher.cs`, `SnapshotPipeClient.cs`, `AlarmScheduler.cs`, `ToastActivator.cs`, `ToastService.cs`, `UpdateChecker.cs`, `HealthCheck.cs`, `TrayCoordinator.cs`, `TrayIconService.cs`, `MainViewModel.cs`, `SettingsPanelViewModel.cs`, `SetupCardViewModel.cs`, `BucketViewModel.cs`, `CalendarViewModel.cs`, `RadialRing.cs`.
- Release/docs: `README.md`, `ROADMAP.md`, `CHANGELOG.md`, `.github/workflows/release.yml`.

## Findings Fixed

- Diagnostics bundle redaction only covered top-level provider IDs and missed schema v3 account arrays; it also left `webhookCommand` secrets intact. Redaction now recurses and redacts webhook commands.
- Snapshot merge dropped schema v2 history and schema v3 multi-account provider lists. Merge now preserves both.
- Health/setup flow only considered `snapshot.json`; local credential-only users could see a stuck setup card. Health now considers the latest of both snapshot files.
- Setup-card dismissals could remain hidden after expiry until a separate health-state change. Refresh now recalculates visibility even when health is unchanged.
- Alarm scheduler ignored schema v3 secondary account lists. It now evaluates primary and multi-account providers.
- Toast action arguments were delimiter-sensitive. Bucket IDs containing `;` or `=` could corrupt snooze/open parsing. Arguments now round-trip through URL encoding.
- Self-updater could select the NMH portable executable because it matched only `-win-{arch}.exe`; it now requires the Widget asset prefix. Its generated PowerShell script now escapes single-quoted URL/path literals.
- Settings reset did not preserve all state promised by the confirmation dialog and did not update every bound property. It now preserves position, autostart, first-run, setup dismissal, reapplies theme, and raises the full settings surface.
- Stale status could not escalate from stale to very-stale because the update was gated only on a boolean stale transition.
- Tray "Refresh now" was a no-op after the coordinator extraction, and badge updates did not follow percent changes on existing cards. Both are wired again.
- Calendar reset entries were never actually sorted inside a day.
- Pace marker calculation could throw when a bucket reported >100% usage.
- Ring warn/danger colors ignored the configured display thresholds.
- Scheduled-task XML was written UTF-8 with a UTF-16 declaration. It now writes UTF-16.
- Release workflow hid missing portable executable copies and could fail on matrix release-creation races.
- README still described v0.1 pre-release behavior; it now reflects current features, build paths, and test count.

## Verification

`dotnet test QuotaGlass.sln --no-restore` with .NET SDK 9.0.314:

- 101 passed
- 0 failed
- 0 skipped

New/expanded tests cover diagnostics redaction, schema v3 merge preservation, multi-account alarms, toast argument parsing, updater guards, setup-card dismissal expiry, calendar ordering, bucket pace marker clamping, and settings reset semantics.

## Remaining Risks

- **R5-P0-02 / R5-N6:** Claude Code OAuth refresh endpoint still needs live validation against a real Claude Code install and credential set. Until verified, users may need to rerun `claude login` if cached local-credential polling expires.
- **N-20 screenshots:** Still needs a live widget session with representative data.
- **CI-triggering PR:** Local tests are green, but the remaining roadmap item specifically requires a GitHub PR workflow run.

## Next State

No unblocked local implementation work remains in the roadmap after this pass. Continue with live OAuth validation or promote a deferred product bet only when demand or runtime evidence justifies it.
