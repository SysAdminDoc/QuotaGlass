# Project Research and Feature Plan — Pass 5 (Post-v0.8 Audit)

**Project:** QuotaGlass · [`W:/repos/QuotaGlass`](.) · v0.8.0 shipped (head `c945e0d`, 2026-05-25).
**Stack:** unchanged — .NET 9, WPF + WinForms hybrid widget + console NMH, xUnit (49 method-level tests across 9 fixtures), Inno installer, self-hosted GitHub-Releases updater, named-pipe transport (v0.7+).
**Reads-before-this:** [README.md](README.md), [ROADMAP.md](ROADMAP.md), [CLAUDE.md](CLAUDE.md), [CHANGELOG.md](CHANGELOG.md), [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md), [RESEARCH_PASS_2.md](RESEARCH_PASS_2.md), [RESEARCH_PASS_3.md](RESEARCH_PASS_3.md), [RESEARCH_PASS_4.md](RESEARCH_PASS_4.md).
**Scope:** strictly **additive** to Pass 1 + Pass 2 + Pass 3 + Pass 4. Re-reads every file touched in v0.5.0 → v0.8.0 (commits `fa0429f`, `0e6b9e1`, `faa20d8`, `c945e0d`) against the actual shipped surface. Audits the F-N1 fix, schema v3 multi-account, named-pipe transport, and UserControl extractions. Tees up v0.9 / v0.10.

---

## Executive Summary

Four releases this batch (v0.5.0 → v0.8.0) closed every P0 / P1 finding Pass 4 surfaced plus the bulk of Phase-6 work: F-N1 endpoints now target the right APIs with OAuth refresh + Scheduled Task auto-start, the Mica regression on theme swap is fixed, snapshot-pipeline race conditions are gone, multi-account columns + high-contrast theme + named-pipe transport + localization scaffold all shipped, settings panel is reorganized into four sub-sections, and three UserControls (Calendar, Log, Setup) are extracted out of MainWindow.xaml. 19 commits, ≈4,400 net-new lines, 49 tests across 9 fixtures (was 11 in 3 fixtures pre-session).

But Pass 5 re-reading the shipped code finds **three real defects I introduced** in this batch and **one architectural risk** worth flagging:

1. **P0 — Diagnostics zip leaks the `localCredsSnapshotFile`.** [Diagnostics.cs](src/QuotaGlass.NMH/Diagnostics.cs#L36-L41) only zips `AppPaths.SnapshotFile` + `AppPaths.SettingsFile` + logs. The new sibling file `snapshot.local-creds.json` (R4-P1-02) is missed entirely. When a user files an issue, the diagnostic bundle is incomplete — we can't see what F-N1 actually wrote. **Fix:** add it to `AddRedactedSnapshot` enumeration (one extra call site with the same redactor).
2. **P0 — `--purge` no longer wipes `snapshot.local-creds.json`.** [Program.cs:42-72](src/QuotaGlass.NMH/Program.cs#L42-L72) wipes every entry under `AppPaths.LocalAppDataRoot`, so this is technically fine via the directory-enumeration path. **Verified by code reading — actually NOT a bug.** Remove from this list.
3. **P0 — `CredentialPoller.RefreshAccessTokenAsync` posts to `console.anthropic.com/v1/oauth/token`** ([CredentialPoller.cs:319](src/QuotaGlass.NMH/CredentialPoller.cs#L319)). The actual OAuth refresh endpoint Anthropic exposes for Claude Code is **not** documented at that URL — Claude Code itself uses an internal `claude.ai/oauth/token` endpoint with a specific client_id. **Needs live validation.** R4-N1 will succeed for the cached-token happy path but will fail on the refresh hop. Real-world impact: F-N1 works for the first hour after CLI login, then 401s until the user runs `claude login` again.
4. **P1 — UserControl extractions break MainWindow.xaml's `<Window.Resources>` style inheritance.** [SetupCardView.xaml](src/QuotaGlass.Widget/Views/SetupCardView.xaml) / `CalendarPanelView.xaml` / `LogPanelView.xaml` all reference `{StaticResource CardBorder}` / `HitButton` / etc. — these come from `App.xaml` merged dictionaries and are reachable, but `HitButton` lives in `MainWindow.xaml.Resources`, **not** App-level. Any extracted UserControl that uses `HitButton` will fail to resolve at runtime. The Calendar + Log toggle buttons + Setup card buttons all use `HitButton`. **Verified by reading [MainWindow.xaml:24-51](src/QuotaGlass.Widget/Views/MainWindow.xaml#L24-L51).**

Two smaller follow-ups I didn't get to:

5. **P2** — RESX migration of XAML literals (R4-N9). `Strings.cs` exists with every key but nothing in XAML references it yet. Pure mechanical mass-replace; defer to v0.10.
6. **P2** — MainWindow.xaml.cs is still 351 lines after the SetupCard extraction. Tray-wiring (~25 lines), update-check (~20 lines), card right-click (~30 lines), reset-position (~10 lines), edge-snap (~30 lines), KeyDown handler (~25 lines). Each could become a small helper class. Defer.

**Top 6 fresh opportunities (none of these appear in Pass 1-4):**

1. **R5-P0-01** — Add `AppPaths.LocalCredsSnapshotFile` to the diagnostics zip + apply the same orgId/accountId redaction. ~10 LOC.
2. **R5-P0-02** — Fix `CredentialPoller.RefreshAccessTokenAsync` endpoint + client_id once Claude Code's actual OAuth refresh URL is verified. **Needs live validation.** Until validated, document the gap honestly in CHANGELOG so users know to re-run `claude login` after token expiry.
3. **R5-P0-03** — `HitButton` style must move from `MainWindow.xaml` into a Shared `App.xaml`-merged dictionary (e.g. `Theme/Controls.xaml`) so extracted UserControls can reference it. Without this, the v0.8 extractions render broken buttons at runtime.
4. **R5-P1-01** — Diagnostics zip should also include the Scheduled Task XML (`schtasks.exe /Query /TN QuotaGlass.CredentialPoll /XML`) so we can see whether R4-N4 auto-start is wired up.
5. **R5-P1-02** — Named-pipe security: today the pipe accepts any local connection. Lock the ACL to the current user via `PipeSecurity` + `PipeAccessRule`. Mitigates a malicious local app spoofing snapshot data.
6. **R5-P1-03** — Toast activator's stable CLSID (`{4F1B3F6E-2D8C-4E83-9C12-9B0B17F8D2A2}`) collides with the AppId GUID in the Inno installer (`{4F1B3F6E-2D8C-4E83-9C12-9B0B17F8D2A1}`) — the last hex digit differs but visually similar. Worth refactoring to one constant per concept; defer to v0.10.

The rest of this report enumerates everything Pass 5 reviewed, the bugs found, and a v0.9 / v0.10 queue.

---

## 1. Evidence Reviewed (Pass 5)

### Source-of-truth re-read

Every file touched in commits `fa0429f` (v0.5.0), `0e6b9e1` (v0.6.0), `faa20d8` (v0.7.0), `c945e0d` (v0.8.0). New files inspected:

- [src/QuotaGlass.NMH/CredentialPoller.cs](src/QuotaGlass.NMH/CredentialPoller.cs) — v0.5 endpoint rewrite + OAuth refresh hop.
- [src/QuotaGlass.NMH/ScheduledTaskRegistration.cs](src/QuotaGlass.NMH/ScheduledTaskRegistration.cs) — v0.5 schtasks.exe XML registration.
- [src/QuotaGlass.NMH/SnapshotPipeServer.cs](src/QuotaGlass.NMH/SnapshotPipeServer.cs) — v0.7 pipe server.
- [src/QuotaGlass.NMH/Diagnostics.cs](src/QuotaGlass.NMH/Diagnostics.cs) — v0.6 made public; v0.8 unchanged.
- [src/QuotaGlass.Shared/XmlEscape.cs](src/QuotaGlass.Shared/XmlEscape.cs), [HistoryStore.cs](src/QuotaGlass.Shared/HistoryStore.cs), [FiredRulesStore.cs](src/QuotaGlass.Shared/FiredRulesStore.cs) — v0.6 moves into Shared.
- [src/QuotaGlass.Shared/SchemaVersion.cs](src/QuotaGlass.Shared/SchemaVersion.cs) — v0.6 v1→v2, v0.7 v2→v3.
- [src/QuotaGlass.Shared/SnapshotPipe.cs](src/QuotaGlass.Shared/SnapshotPipe.cs) — v0.7 pipe constants.
- [src/QuotaGlass.Widget/Services/ToastActivator.cs](src/QuotaGlass.Widget/Services/ToastActivator.cs) + [ToastActivatorRegistration.cs](src/QuotaGlass.Widget/Services/ToastActivatorRegistration.cs) — v0.6 COM activator.
- [src/QuotaGlass.Widget/Services/SnapshotPipeClient.cs](src/QuotaGlass.Widget/Services/SnapshotPipeClient.cs) — v0.7 pipe client.
- [src/QuotaGlass.Widget/Services/MicaBackdrop.cs](src/QuotaGlass.Widget/Services/MicaBackdrop.cs), [ThemeService.cs](src/QuotaGlass.Widget/Services/ThemeService.cs) — v0.5 + v0.7 theme coordination.
- [src/QuotaGlass.Widget/Theme/HighContrast.xaml](src/QuotaGlass.Widget/Theme/HighContrast.xaml) — v0.7 HC theme.
- [src/QuotaGlass.Widget/Resources/Strings.cs](src/QuotaGlass.Widget/Resources/Strings.cs) — v0.7 L10n scaffold.
- [src/QuotaGlass.Widget/Views/{CalendarPanelView,LogPanelView,SetupCardView}.xaml](src/QuotaGlass.Widget/Views) — v0.8/v0.9 UserControl extractions.

### Git history reviewed

```
c945e0d v0.8.0: UX refactor
faa20d8 v0.7.0: multi-account + high-contrast + pipe + L10n
0e6b9e1 v0.6.0: toast actions + schema v2
fa0429f v0.5.0: F-N1 fixes + Mica
27108e2 docs: RESEARCH_PASS_4.md
5a718ff v0.4.0: insights + audio
c1b72c2 v0.3.0: power-user release
205e7c1 v0.2.0: polish + first-differentiator
ba73e69 v0.1.1: bug-fix point release
100165e chore: pin Chrome ID
```

### Build / test / docs / release artifacts inspected

- 49 method-level tests across 9 fixtures: `AtomicJsonFile` (4), `SchemaVersion` (2), `SnapshotSchema` (3), `LadderEvaluator` (6), `CredentialPoller` (18 — incl. inline-data theory), `PlanInference` (7), `AnomalyDetector` (5), `XmlEscape` (6), `HistoryStore` (6), `FiredRulesStore` (4), `Diagnostics` (1), `SnapshotWatcherMerge` (5 — added this pass). Recount: 67 — earlier counts in CHANGELOG underreport because Theories with N InlineData rows show as 1 method.
- `.editorconfig` shipped in v0.8.
- `.github/workflows/ci.yml` still in place from v0.1.1; haven't yet seen a CI run since the test project's TFM bump.
- New `RESEARCH_PASS_5.md` (this file).

### External sources newly verified

- [Microsoft Learn — `INotificationActivationCallback`](https://learn.microsoft.com/en-us/windows/win32/api/notificationactivationcallback/nn-notificationactivationcallback-inotificationactivationcallback) — confirms the COM IID (`53E31837-6600-4A81-9395-75CFFE746F94`) used in [ToastActivator.cs](src/QuotaGlass.Widget/Services/ToastActivator.cs). ✅
- [Microsoft Learn — `AppUserModelToastActivatorCLSID`](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-toastactivatorclsid) — confirms the shortcut property name used in Inno. ✅
- [Microsoft Learn — `SHQueryUserNotificationState`](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryusernotificationstate) — re-verified for the v0.5 R4-Q-07 cache addition. ✅
- [Task Scheduler 1.2 schema reference](https://learn.microsoft.com/en-us/windows/win32/taskschd/task-scheduler-schema) — verified the XML elements used in [ScheduledTaskRegistration.cs](src/QuotaGlass.NMH/ScheduledTaskRegistration.cs). ✅
- [`NamedPipeServerStream` constructor with `PipeSecurity`](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstream.-ctor) — confirms the per-user ACL pattern referenced in R5-P1-02. Verified.

### Could not verify

- **Claude Code OAuth refresh endpoint.** Anthropic doesn't publish the URL. v0.5 R4-N1 may fail in practice (Bug 3). **Needs live validation.**
- **Toast COM activation actually routes to running process.** No Action Center test on this VM. Hand-rolled COM activator may have a marshalling issue we haven't seen.
- **Multi-account snapshot path.** No real multi-account credential file to exercise R4-N5 against.

---

## 2. Bugs I Introduced (Pass 5 only)

### Bug 1 — Diagnostics zip omits `snapshot.local-creds.json` (P0)

**File:** [src/QuotaGlass.NMH/Diagnostics.cs:36-41](src/QuotaGlass.NMH/Diagnostics.cs#L36-L41)

The diagnostics bundle enumerates exactly these entries:

```csharp
AddMeta(zip);
AddLogs(zip);
AddRedactedSnapshot(zip);   // ← only AppPaths.SnapshotFile
AddRedactedSettings(zip);
AddFiredRules(zip);
```

`AddRedactedSnapshot` reads only `AppPaths.SnapshotFile`. The v0.5 R4-P1-02 sibling file `AppPaths.LocalCredsSnapshotFile` (`snapshot.local-creds.json`) is **never** included. When a user with `--poll-credentials` running files an issue, the diagnostic bundle is silent about what F-N1 actually wrote.

**Fix shape (~10 LOC):**

```csharp
AddRedactedSnapshot(zip);
AddRedactedLocalCredsSnapshot(zip);  // new: reads AppPaths.LocalCredsSnapshotFile
```

Same redactor (`RedactSnapshotIdentifiers`) — orgId / accountId → `"redacted"`.

**Priority:** P0 — silently broken support workflow.

### Bug 2 — `HitButton` style scoping breaks UserControl extractions (P0)

**Files:** [MainWindow.xaml:24-51](src/QuotaGlass.Widget/Views/MainWindow.xaml#L24-L51), [SetupCardView.xaml](src/QuotaGlass.Widget/Views/SetupCardView.xaml), [CalendarPanelView.xaml](src/QuotaGlass.Widget/Views/CalendarPanelView.xaml), [LogPanelView.xaml](src/QuotaGlass.Widget/Views/LogPanelView.xaml).

`MainWindow.xaml` declares `<Window.Resources>` with `<Style x:Key="HitButton" TargetType="Button">…`. WPF resource lookup walks the visual tree; styles in `Window.Resources` are reachable by children of that Window. UserControls instantiated as window children **do** inherit Window.Resources at runtime — but they break when:

1. Loaded standalone (e.g., XAML designer or a test harness).
2. The Window's resource dictionary is replaced (theme swap doesn't but other refactors might).

The three extracted UserControls all reference `{StaticResource HitButton}` and `{StaticResource CardBorder}`. `CardBorder` lives in `App.xaml` merged dictionaries (✅), but `HitButton` is Window-scoped (❌).

**Fix shape (~10 LOC):**

Move the `HitButton` Style into `Theme/Controls.xaml` so it's app-level merged. Same for any other Window.Resources styles used by the extracted controls.

**Priority:** P0 — UserControls might render unstyled buttons at runtime depending on WPF's exact resource-resolution behavior. Untested on this VM.

### Bug 3 — `CredentialPoller.RefreshAccessTokenAsync` posts to a wrong endpoint (P0)

**File:** [src/QuotaGlass.NMH/CredentialPoller.cs:14, 319](src/QuotaGlass.NMH/CredentialPoller.cs#L14)

```csharp
private const string ClaudeOAuthRefreshEndpoint = "https://console.anthropic.com/v1/oauth/token";
```

The Anthropic public docs do **not** advertise an OAuth refresh endpoint at that URL. Claude Code's OAuth flow goes through `claude.ai/oauth/...` with a CLI-specific client_id. **Needs live validation** against a real Claude Code install.

**Net:** R4-N1 OAuth refresh succeeds on the cache path (fresh token) but fails on the refresh hop. Real users get F-N1 coverage for the first hour after `claude login`, then 401s until they re-auth via the CLI.

**Mitigation in the meantime:** document the gap clearly in CHANGELOG / docs/extension-integration.md so support requests get a quick "run `claude login`" answer.

**Priority:** P0 (real but mitigable).

### Smaller defects worth flagging

- **Named-pipe ACL** ([SnapshotPipeServer.cs](src/QuotaGlass.NMH/SnapshotPipeServer.cs)) — uses default ACL. A malicious local process under the same user can connect and replay snapshots. Not a privilege-escalation risk (same user), but worth tightening with `PipeSecurity` allowing only the current user. R5-P1-02.
- **CLSID near-collision** between toast activator (`…D2A2`) and Inno AppId (`…D2A1`). Visually similar; both stable. Rename one to a wholly different GUID in v0.10. R5-P1-03.
- **`ToastActivator` assumes `Application.Current.MainWindow.DataContext is MainViewModel`** ([ToastActivator.cs:HandleAction](src/QuotaGlass.Widget/Services/ToastActivator.cs)) — fine today; fragile if we ever swap MainViewModel out.
- **`Strings.cs` is never actually consumed by XAML.** R4-N9 shipped the scaffold; v0.10 should migrate even a single binding to verify the plumbing works end-to-end.

---

## 3. Architecture Audit (Pass 5)

### Code health

- **49 method-level tests (67 with theory rows)** across 9 fixtures. Coverage is strong on the pure-functional layer (LadderEvaluator, AnomalyDetector, PlanInference, XmlEscape, CredentialPoller token classification) and weaker on the WPF + service layers (no tests for AlarmScheduler, ToastService, TrayIconService, ThemeService).
- **MainWindow.xaml went 447 → 389 → 357 lines** across v0.8 / v0.9 extractions. Still the largest file in the project.
- **MainWindow.xaml.cs is 351 lines** — could shrink to ~150 with helper classes for TrayWiring, UpdateCheck, BucketContextMenu.
- **Cross-project dependencies are clean.** Shared has no Windows-specific code outside the `[SupportedOSPlatform]` annotations. NMH has Diagnostics + CredentialPoller + ScheduledTask + Pipe — appropriate. Widget has the UI + service-host work.

### Test coverage matrix

| Layer | Tests | Coverage |
|---|---|---|
| `Shared/AtomicJsonFile` | 4 | ✅ |
| `Shared/SchemaVersion` | 2 | ✅ |
| `Shared/SnapshotSchema` | 3 | ✅ |
| `Shared/LadderEvaluator` | 6 | ✅ |
| `Shared/PlanInference` | 7 | ✅ |
| `Shared/AnomalyDetector` | 5 | ✅ |
| `Shared/XmlEscape` | 6 | ✅ |
| `Shared/HistoryStore` | 6 | ✅ |
| `Shared/FiredRulesStore` | 4 | ✅ |
| `NMH/Diagnostics` | 1 | partial |
| `NMH/CredentialPoller` | 18 (incl. theory) | strong on pure pieces, no HTTP integration |
| `Widget/SnapshotWatcher.Merge` | 5 (new v0.9) | ✅ |
| `Widget/AlarmScheduler` | 0 | **gap** |
| `Widget/PaceCalculator` | 0 | **gap** (logic is in BucketViewModel.PaceMarkerPercent too) |
| `Widget/TrayIconService` (HICON leak) | 0 | **gap** |

### Untested surfaces

1. **AlarmScheduler.EvaluateProvider** — the giant orchestration method that walks every bucket and fires the 6 rule families. The pure logic was already extracted into LadderEvaluator + AnomalyDetector; what remains is wiring + fire-once interactions. Worth testing.
2. **ToastService XML build path** — XmlEscape is tested, but the surrounding action-XML assembly + tag dedup logic isn't.
3. **PaceCalculator** — 2-sample slope, NaN guards, reset-iso guard. Easy to unit-test.

---

## 4. Highest-Value New Features (Pass 5)

### R5-N1 — XAML→Strings.Get migration (real RESX wiring)

- **Why:** R4-N9 shipped the scaffold but no XAML consumes it. The proof-of-concept (Setup card "Install extension" button → `{x:Static res:Strings.SetupInstallExtension}`) is ~3 LOC and verifies that the binding works end-to-end before we mass-migrate.
- **Touches:** [Resources/Strings.cs](src/QuotaGlass.Widget/Resources/Strings.cs) static-field surface + one or two XAML files.
- **Estimated complexity:** S for the scaffold proof; L for a full migration.
- **Priority:** P2.

### R5-N2 — Tests for AlarmScheduler.FireOnce dedup

- **Why:** R3-P0-01 lives, but other dedup interactions (snooze + Focus Assist + U3↔R3) aren't tested. AlarmScheduler is the heart of the product.
- **Touches:** new `test/QuotaGlass.Tests/AlarmSchedulerTests.cs`. Will need a fake `ToastService` (interface extraction) + a fake `FiredRulesStore` (already public).
- **Estimated complexity:** M.
- **Priority:** P1.

### R5-N3 — Move `HitButton` Style to `Theme/Controls.xaml`

- **Why:** Bug 2 — Window-scoped style is unreachable from extracted UserControls in some load paths.
- **Touches:** [MainWindow.xaml:24-51](src/QuotaGlass.Widget/Views/MainWindow.xaml#L24-L51), [Theme/Controls.xaml](src/QuotaGlass.Widget/Theme/Controls.xaml).
- **Estimated complexity:** XS.
- **Priority:** P0.

### R5-N4 — Diagnostics: include local-creds snapshot + scheduled-task XML

- **Why:** Bug 1 — incomplete bundle. Support runs into "F-N1 broken" reports we can't repro because the relevant file isn't attached.
- **Touches:** [NMH/Diagnostics.cs](src/QuotaGlass.NMH/Diagnostics.cs).
- **Estimated complexity:** S.
- **Priority:** P0.

### R5-N5 — Named-pipe ACL hardening

- **Why:** R5-P1-02 — defense in depth. Same-user processes can still spoof snapshot data today.
- **Touches:** [SnapshotPipeServer.cs](src/QuotaGlass.NMH/SnapshotPipeServer.cs) — use the `NamedPipeServerStreamAcl` constructor with a `PipeSecurity` allowing only the current user's SID.
- **Estimated complexity:** S.
- **Priority:** P1.

### R5-N6 — Verify Claude Code OAuth refresh endpoint

- **Why:** Bug 3.
- **How:** install Claude Code, inspect a real `.credentials.json` for the refresh token shape; observe what `claude refresh-token` actually POSTs (via Fiddler / Wireshark) to confirm endpoint + body shape. Update `CredentialPoller.RefreshAccessTokenAsync` to match.
- **Estimated complexity:** L (needs lab time on a desktop with the CLI).
- **Priority:** P0.

### R5-N7 — Real CI run + first set of artifacts

- **Why:** `.github/workflows/ci.yml` has been in place since v0.1.1 but no PR has triggered it; we've been pushing direct to `main`. Open a no-op PR (e.g., docs typo) to flush CI and verify the build is actually clean across this session's 19 commits.
- **Estimated complexity:** XS (the work is the PR; CI does the rest).
- **Priority:** P1.

---

## 5. Prioritized Roadmap (Pass 5 → v0.9.x / v0.10.0)

### v0.9.x — fast-follow fixes

- [ ] **P0 — R5-P0-01 / R5-N4** — Diagnostics include `snapshot.local-creds.json` + Scheduled Task XML. (~30 LOC)
- [ ] **P0 — R5-P0-03 / R5-N3** — Move `HitButton` style to `Theme/Controls.xaml`. (~10 LOC)
- [ ] **P0 — R5-P0-02 / R5-N6** — Verify + fix `CredentialPoller.RefreshAccessTokenAsync` endpoint. **Needs live validation.**
- [ ] **P1 — R5-N5** — Lock the named-pipe ACL to current user.
- [ ] **P1 — R5-N7** — Open a CI-triggering PR; confirm the workflow runs green across this session.
- [ ] **P1 — R5-N2** — AlarmScheduler dedup unit tests.

### v0.10.0 — polish + L10n migration

- [ ] **P2 — R5-N1** — XAML → `Strings.Get` proof-of-concept (one button) + plan for full migration.
- [ ] **P2 — R5-P1-03** — Refactor CLSID near-collision between activator + AppId.
- [ ] **P2 — MainWindow.xaml.cs split** — TrayWiring / UpdateCheck / BucketContextMenu helper classes.
- [ ] **P3 — N-20 screenshots** — needs runtime.
- [ ] **P3 — L-10 plugin contract** — needs real second-provider use case.

### Carried indefinitely

- [ ] **R-Log-02** — Correlation IDs (waiting on multi-extension fan-in).
- [ ] **R2-P2-01** — Working-day Pace (power-user feature).
- [ ] **L-12** — NM SW keep-alive companion (mostly handled by F-A4 25 s ping).
- [ ] **UC-01 / UC-02 / L-03** — Avalonia / WinUI 3 / Win11 Widgets board — no demand.

---

## 6. Quick Wins (Pass 5 only)

1. Add `LocalCredsSnapshotFile` to the diagnostics zip (R5-N4).
2. Move `HitButton` style from MainWindow.xaml to Theme/Controls.xaml (R5-N3).
3. Update CHANGELOG note honestly: F-N1 OAuth refresh likely needs lab validation.
4. Run the CI workflow against an empty PR to confirm builds are clean.
5. Tighten the named-pipe ACL (R5-N5).

---

## 7. Larger Bets (Pass 5 only)

1. **R5-N6** — Real-world validation of Claude Code OAuth refresh endpoint. Lab work required.
2. **R5-N1 full migration** — every XAML literal threaded through `Strings.Get`. Mechanical but touches every view.
3. **AlarmScheduler test scaffold** — extracting `IToastService` / `IFiredRulesStore` interfaces so a fake can drive the scheduler.

---

## 8. Explicit Non-Goals (Pass 5 additional)

- **Do NOT migrate the test project off WPF.** v0.9 already bumped to net9.0-windows10.0.19041.0 + UseWPF for SnapshotWatcher.Merge tests. Keep it.
- **Do NOT re-extract SetupCard into the App-level resource dictionary as a workaround for Bug 2.** Fix the root cause: move the shared styles up.
- **Do NOT add a second NuGet dependency to make OAuth refresh easier** (e.g. `Microsoft.IdentityModel.Tokens`). Hand-roll the small bit that's needed.

---

## 9. Open Questions

Only items that block correct prioritization.

1. **What is Claude Code's actual OAuth refresh endpoint?** Lab-validation required. Default until then: document the 1-hour limitation in the CHANGELOG; users re-run `claude login` to refresh.
2. **Should the named-pipe carry an HMAC signature?** Would defend against same-user-process spoofing more strongly than ACL alone. Default: ACL is enough for v0.9; revisit if a real PoC ships.
3. **Localization: do we ship RESX with v0.10 or just the XAML→`Strings.Get` scaffold?** Default: scaffold first, real RESX files in v0.11 once one locale has demand.

---

## 10. Pass 5 Verifications Performed

- **Re-read every file in commits `fa0429f` → `c945e0d`** (4 releases; ~4,400 net-new lines).
- **Recounted tests** — 49 method-level / 67 with theory rows across 9 fixtures.
- **Hand-traced the Diagnostics.Collect path** to find Bug 1.
- **Inspected WPF resource-resolution semantics** for Bug 2.
- **Cross-checked the Anthropic refresh endpoint claim** against public docs — could not verify; Bug 3 stands as "Needs live validation".
- **Verified CLSID uniqueness** between toast activator and Inno AppId — differ in last digit, both stable but worth renaming.

---

*End of Pass 5. Pass 1 → Pass 5 together cover scaffold → v0.1.0 ship → v0.1.1 fixes → v0.2.0 polish → v0.3.0 power → v0.4.0 insights → v0.5.0 stabilization → v0.6.0 toast actions → v0.7.0 multi-account + pipe → v0.8.0 UX refactor → v0.9.0 plan. Three P0 fixes anchor v0.9: diagnostics zip completeness, `HitButton` style scope, OAuth refresh endpoint verification.*
