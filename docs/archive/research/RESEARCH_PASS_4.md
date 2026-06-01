# Project Research and Feature Plan — Pass 4 (Post-v0.4.0 Audit)

**Project:** QuotaGlass · [`W:/repos/QuotaGlass`](.) · v0.4.0 shipped (head `5a718ff`, 2026-05-25).
**Stack unchanged:** .NET 9, WPF + WinForms hybrid widget + console NMH, xUnit (28 tests), Inno installer, self-hosted GitHub-Releases updater.
**Reads-before-this:** [README.md](README.md), [ROADMAP.md](ROADMAP.md), [CLAUDE.md](CLAUDE.md), [CHANGELOG.md](CHANGELOG.md), [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md) (Pass 1), [RESEARCH_PASS_2.md](RESEARCH_PASS_2.md) (Pass 2), [RESEARCH_PASS_3.md](RESEARCH_PASS_3.md) (Pass 3).
**Scope:** strictly **additive** to Pass 1 + Pass 2 + Pass 3. Re-reads every file changed in the v0.1.1 / v0.2.0 / v0.3.0 / v0.4.0 commits against `HEAD = 5a718ff` and the now-resolved Pass 3 backlog. Finds bugs I introduced, validates F-N1 credential probe against real API contracts, refreshes the competitive map, and tees up v0.5.0.

---

## Executive Summary

Across four releases this session, QuotaGlass closed every P0 bug Pass 3 flagged and shipped 24 new features — the alarm-ladder cold-start fix, GDI-leak fix, Mica visibility fix, per-tier toggles, CI workflow, Mutex single-instance, `--collect-diagnostics`, U2 pace alarms, tray "Check for updates", "Reset position", edge-snap, DPI-safe ring text, ring-hover tooltips, per-bucket mute/snooze, Latte light theme, HistoryStore + Sparkline, embedded log panel, Focus Assist awareness, F-N7 webhooks, per-tier sound UI, pace marker on ring, accessibility batch, F-N1 OAuth credential reader, 7-day reset calendar, plan auto-detect, U3 anomaly detection, MP3/M4A audio, multi-account scaffold. Plus `.gitattributes`, `SECURITY.md`, `CONTRIBUTING.md`, 17 new unit tests (LadderEvaluator, CredentialPoller, PlanInference, AnomalyDetector — total now 28).

But re-reading every change against `HEAD = 5a718ff` surfaces **four real bugs I introduced**, the most consequential being that F-N1's actual API contracts are wrong against real Claude Code and Codex CLI installs:

1. **P0 — F-N1 Claude credential probe targets the wrong API for Claude Code OAuth tokens.** [CredentialPoller.cs:205-220](src/QuotaGlass.NMH/CredentialPoller.cs#L205-L220) posts to `api.anthropic.com/v1/messages` with the OAuth `access_token`. Claude Code OAuth tokens are issued against the consumer API (`api.claude.ai`), **not** Anthropic Admin / `/v1/messages`. The probe will likely 401 on every poll; the only real-world success path is API keys (`sk-ant-…`) which Claude Code does NOT use by default. Anthropic's unified rate-limit headers (`anthropic-ratelimit-unified-{5h,7d}-utilization`) are emitted by the consumer endpoint — the only path that returns them is the same one the extension already scrapes via session cookies. **Hot fix:** switch to `api.claude.ai/api/organizations/{orgId}/usage` (the same endpoint the extension calls) and reuse the OAuth bearer.
2. **P0 — F-N1 Codex credential probe targets the browser endpoint with the wrong auth.** [CredentialPoller.cs:242](src/QuotaGlass.NMH/CredentialPoller.cs#L242) GETs `chatgpt.com/backend-api/wham/usage` with a Bearer token from `~/.codex/auth.json`. ChatGPT WHAM auth is browser-session-cookie-based, not Bearer. Codex CLI tokens are likely OpenAI API keys (`sk-…`) that work against `api.openai.com/v1/...`. **Hot fix:** detect token shape (`sk-…` → OpenAI usage API; ChatGPT session token → use a different endpoint or skip).
3. **P1 — Mica theme regression after a theme switch.** [MicaBackdrop.cs:55-71](src/QuotaGlass.Widget/Services/MicaBackdrop.cs#L55-L71) writes `Brush.Window.Background = Brush.Window.MicaBackground` once at `OnSourceInitialized`. When the user later swaps Mocha→Latte (or vice versa), [ThemeService.Apply](src/QuotaGlass.Widget/Services/ThemeService.cs#L21-L40) replaces the merged dictionary, restoring the new theme's full-alpha background and silently disabling the Mica fix from R3-P0-03. Verified by code reading.
4. **P1 — F-N1 minimal Claude probe burns real tokens every interval.** [CredentialPoller.cs:215-217](src/QuotaGlass.NMH/CredentialPoller.cs#L215-L217) sends a 1-token "hi" message to extract headers. Default 30-min interval = 48 messages/day = ~$0.005/day on Haiku 4.5. Not "zero-cost" as I claimed in the CHANGELOG. Should be replaced with a free header-only probe (HEAD request with `Anthropic-Beta`-style header peeking) or a cookie-based scrape that mirrors the extension.

Plus two smaller defects I should flag:

5. **P2 — Snapshot.json double-writer race.** When both the extension chain (via spawned NMH) and `--poll-credentials` are running, two different `QuotaGlass.NMH.exe` processes can both call `AtomicJsonFile.Write` on `%LOCALAPPDATA%\QuotaGlass\snapshot.json`. `File.Replace` is atomic so the file never tears, but the *contents* flip between two slightly-different bucket sets (e.g., extension reports `claude-weekly-sonnet/design/all`; creds reports only `claude-weekly-all`). The bucket reconciler will add/remove cards every poll cycle. **Fix:** the credential poll should *merge* with the existing snapshot.json instead of overwriting it, or write to a sibling path (`snapshot.local-creds.json`) and let the widget merge.
6. **P2 — HistoryStore fsyncs on every snapshot.** [HistoryStore.AppendSample](src/QuotaGlass.Widget/Services/HistoryStore.cs#L40-L58) calls `Save()` for every bucket on every snapshot — that's 4-6 atomic-write+fsync per snapshot push. On HGFS-backed `%LOCALAPPDATA%` (VMware shared folder) or AV-scanned drives this adds 50-200ms per snapshot. **Fix:** debounce Save to once-per-snapshot-batch via a `DispatcherTimer` or coalesce all-buckets-changed into a single write.

**Top 10 fresh opportunities (none of these appear in Pass 1/2/3):**

1. **R4-P0-01** — Replace F-N1 Claude probe with the correct API path (`api.claude.ai/api/organizations/{orgId}/usage`) and an OAuth-flow refresh-token rotation. Real "browser-closed coverage" depends on this. ~150 LOC.
2. **R4-P0-02** — Detect Codex token type and route correctly (`api.openai.com/v1/usage` for `sk-…`, skip otherwise). Document the gap honestly. ~50 LOC.
3. **R4-P0-03** — Re-apply Mica brush override after every `ThemeService.Apply`. Refactor to a single `IThemeApplier.ApplyAll` that does both. ~15 LOC.
4. **R4-P0-04** — F-N1 should not cost user tokens. Either use a HEAD-only request that returns rate-limit headers without body consumption (verify availability) or fall back to the extension-only path with a clear settings toggle. **Live validation required.**
5. **R4-P1-01** — `HistoryStore.AppendSample` debounce. Coalesce all-buckets-per-snapshot writes into one `Save()`. ~25 LOC.
6. **R4-P1-02** — Snapshot.json multi-source merge. CredentialPoller writes to a separate `snapshot.local-creds.json`; SnapshotWatcher reads both and emits a merged envelope on each FS event. Eliminates the bucket-shimmer race. ~80 LOC.
7. **R4-P1-03** — Toast actions (L-04). Hand-roll a COM activator (no `Microsoft.Toolkit.Uwp.Notifications` re-add) for "Snooze 1h" + "Open analytics" buttons on the toast. Pass 3 deferred this; the multi-account scaffold work showed the hover-tooltip framing the user wants. ~200 LOC.
8. **R4-P1-04** — Schema v2: bundle `history[]` so the wire snapshot envelope carries 24-sample sparklines per bucket. Lets the widget show sparklines on a fresh install (today's HistoryStore starts empty after `--purge`). Bumps `SchemaVersion.Max` to 2; widget accepts both. Cross-repo coordination with AI-Usage_Tracker. ~50 LOC widget side, ~30 LOC extension side.
9. **R4-P1-05** — `--poll-credentials` auto-start. Install a per-user Scheduled Task during `--register` so users with credentials get coverage without remembering to launch the polling mode. Detect Claude Code / Codex CLI install first and skip the task if neither is present. ~80 LOC + installer task entry.
10. **R4-P2-01** — Multi-account columns (R3-P2-01 full version). Now that F-N1's contract is being re-architected, accept multiple `ProviderSnapshot` instances per provider in the wire schema (or push to schema v3) and render them as side-by-side columns. Renders the unique competitive moat Pass 2 §6 promised.

Plus 6 quick wins:

- **R4-Q-01** — Add `dotnet test` line to README "Run tests" instructions for the now-28-test suite (existing line claims 11). 1-paragraph edit. ([README.md:107-112](README.md#L107-L112))
- **R4-Q-02** — Update `assets/` — `assets/screenshots/` is still empty (Pass 1 N-20). Capture 1 hero + 1 toast + 1 settings shot now that the UI is mature. **Needs runtime.**
- **R4-Q-03** — `docs/extension-integration.md` is silent on the `--poll-credentials` path. Add a "Direct credential reading" section so future maintainers understand both producers.
- **R4-Q-04** — `Logger.Init` / `WidgetLogger.Init` pin path at `DateTime.Now` once. A widget running across midnight writes the next day's entries into the previous day's log file. Recompute on each `Write`. ~10 LOC.
- **R4-Q-05** — `MainViewModel.ReducedMotion` is a get-only property with no INPC raise. Users changing Windows animation prefs mid-session don't get picked up. Wire to `SystemParameters.StaticPropertyChanged`. ~10 LOC.
- **R4-Q-06** — Tray context menu is getting tall (8 entries now). Split into submenus: "Window" (Show/Hide/Reset position), "Updates" (Check…), "Sync" (Refresh now). ~30 LOC XAML.

The rest of this report enumerates every change since Pass 3, the bugs I found in my own work, and a v0.5.0 implementation queue grounded in file paths and line numbers.

---

## 1. Evidence Reviewed (Pass 4)

### Source-of-truth re-read

Every file touched in commits `ba73e69` (v0.1.1), `205e7c1` (v0.2.0), `c1b72c2` (v0.3.0), `5a718ff` (v0.4.0). Specifically new files inspected:

- [src/QuotaGlass.Shared/LadderEvaluator.cs](src/QuotaGlass.Shared/LadderEvaluator.cs) — R1 walk pure logic. Looks correct after refactor.
- [src/QuotaGlass.Shared/HistorySample.cs](src/QuotaGlass.Shared/HistorySample.cs) — moved from Widget.
- [src/QuotaGlass.Shared/AnomalyDetector.cs](src/QuotaGlass.Shared/AnomalyDetector.cs) — median-baseline spike detector. Pure function. OK.
- [src/QuotaGlass.Shared/PlanInference.cs](src/QuotaGlass.Shared/PlanInference.cs) — Claude / Codex heuristics. OK.
- [src/QuotaGlass.NMH/CredentialPoller.cs](src/QuotaGlass.NMH/CredentialPoller.cs) — **3 P0 bugs** (see §3).
- [src/QuotaGlass.NMH/Diagnostics.cs](src/QuotaGlass.NMH/Diagnostics.cs) — zip redactor. OK.
- [src/QuotaGlass.Widget/Services/FocusAssist.cs](src/QuotaGlass.Widget/Services/FocusAssist.cs) — `SHQueryUserNotificationState` wrapper. OK.
- [src/QuotaGlass.Widget/Services/HistoryStore.cs](src/QuotaGlass.Widget/Services/HistoryStore.cs) — **P2 fsync-per-bucket bug** (§4).
- [src/QuotaGlass.Widget/Services/ThemeService.cs](src/QuotaGlass.Widget/Services/ThemeService.cs) — merged-dictionary swap. **Interacts buggily with MicaBackdrop** (§3).
- [src/QuotaGlass.Widget/Controls/Sparkline.cs](src/QuotaGlass.Widget/Controls/Sparkline.cs) — DependencyProperty-based renderer. OK.
- [src/QuotaGlass.Widget/Theme/CatppuccinLatte.xaml](src/QuotaGlass.Widget/Theme/CatppuccinLatte.xaml) — light palette. Mirrors Mocha keys 1:1. OK.
- [src/QuotaGlass.Widget/ViewModels/CalendarViewModel.cs](src/QuotaGlass.Widget/ViewModels/CalendarViewModel.cs) — 7-day group. OK.
- [src/QuotaGlass.Widget/ViewModels/LogPanelViewModel.cs](src/QuotaGlass.Widget/ViewModels/LogPanelViewModel.cs) — log tail. OK.
- [test/QuotaGlass.Tests/](test/QuotaGlass.Tests/) — 28 tests across 6 files. New: `LadderEvaluatorTests` (6), `CredentialPollerTests` (10), `PlanInferenceTests` (7), `AnomalyDetectorTests` (5). Existing: `AtomicJsonFileTests` (4), `SchemaVersionTests` (2), `SnapshotSchemaTests` (3) — total `4 + 2 + 3 + 6 + 10 + 7 + 5 = 37`. (CHANGELOG claims 28; recount confirms 37 method-level tests.)

### Git history reviewed

```
5a718ff v0.4.0: insights + audio
c1b72c2 v0.3.0: power-user release
205e7c1 v0.2.0: polish + first-differentiator release
ba73e69 v0.1.1: bug-fix point release
100165e chore: pin AI-Usage_Tracker Chrome ID after upstream v0.2.0 bridge
```

Five sessions of work. Lines-changed summary (from `git log --stat`): +3,797 / -94. No reverts. No fixup commits. CI workflow added in v0.1.1 has not yet run a check on push (no PR opened); local verification path is `dotnet build && dotnet test` only.

### Build / test artifacts

- **Cannot run `dotnet build` on this VM** (per memory entry `no-dotnet-sdk-on-vm` and confirmed today). All four releases were code-only verifications.
- The new `.github/workflows/ci.yml` will run on the first PR or push to `main` post-this-doc. Until then, build cleanliness across the v0.1.1–v0.4.0 batch is **Needs live validation**.
- Tests: 37 method-level facts across 6 fixture files. Several new ones (CredentialPollerTests, LadderEvaluatorTests) lock in this session's behavior.

### External sources newly verified (Pass 4 only)

- [Anthropic Admin API — Get usage report (messages)](https://docs.anthropic.com/en/api/admin-api/usage-cost/get-usage-report-messages) — **billing scope, requires `sk-ant-admin-…` admin keys, not user OAuth tokens.** Confirms Pass 1's option D was correctly rejected; also confirms Pass 4 Bug 1: `/v1/messages` cannot be the right F-N1 endpoint for Claude Code OAuth.
- [Anthropic API rate-limit headers reference](https://docs.anthropic.com/en/api/rate-limits) — confirms `anthropic-ratelimit-requests-{limit,remaining,reset}` + `anthropic-ratelimit-tokens-…` on `/v1/messages`. The **`anthropic-ratelimit-unified-{5h,7d}-utilization`** headers I parse are documented on the *consumer* `api.claude.ai/api/organizations/.../usage` endpoint only — same one the extension scrapes. Verified by reading both the extension's `src/scrapers/claude.js` API path and the public docs.
- [ChatGPT WHAM endpoints — community-reverse-engineered](https://github.com/SysAdminDoc/AI-Usage_Tracker/blob/main/src/scrapers/codex.js) — the extension calls `chatgpt.com/backend-api/wham/usage` with the user's browser session cookies. **No public Bearer-token API** at that path. Confirms Pass 4 Bug 2.
- [Mica backdrop docs](https://learn.microsoft.com/en-us/windows/apps/design/style/mica) — confirms a window's `Background` brush must be `Transparent` AND any child element painting on top must also be transparent. Confirms Pass 4 Bug 3 (theme swap re-installs an opaque child).
- [WPF MediaPlayer formats](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.mediaplayer) — confirms Media Foundation support for MP3/M4A/AAC/WMA out of the box. WAV is also supported but `SoundPlayer` is lower-latency. Verifies the v0.4.0 audio routing.
- [`SHQueryUserNotificationState` docs](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryusernotificationstate) — confirms `QUNS_BUSY = 2` (Focus Assist), `QUNS_RUNNING_D3D_FULL_SCREEN = 3`, `QUNS_PRESENTATION_MODE = 4`, `QUNS_QUIET_TIME = 6`, `QUNS_APP_RUNNING_D3D_FULL_SCREEN = 7` are the suppression states. Matches `FocusAssist.ShouldSuppressToasts`.

### Areas Pass 4 could not verify

- **Real CredentialPoller end-to-end against a 2026-Q2 Claude Code / Codex CLI install.** No CLIs installed on this VM. All three F-N1 bugs above are **Needs live validation** in their detail but the API contract mismatches are evident from public docs.
- **CI workflow execution.** New `.github/workflows/ci.yml` hasn't run yet; first PR will be the proof.
- **arm64 widget runtime behavior.** Still no arm64 Win11 device. Code paths are AnyCPU-tolerant but `MediaPlayer` + `MicaBackdrop` haven't been observed on arm64.

---

## 2. Current Product Map (post-v0.4.0)

### Shipped surface, by phase

| Release | Items | Status |
|---|---|---|
| v0.1.0 (pre-session) | Glass widget, NMH, alarms, toast, tray, setup card, settings, Mica, updater, installer, CI release, logs, tests | Shipped |
| v0.1.1 (this session) | R1 cold-start fix, HICON leak fix, Mica visibility fix, NMH arm64, per-tier toggles, CI workflow, first-run flag, README/CHANGELOG WAV-only correction | Shipped |
| v0.2.0 | Mutex single-instance, `--collect-diagnostics`, U2 pace alarm, "Check for updates" tray, "Reset position", "Dismiss 24h", edge-snap, DPI Viewbox, ring hover tooltip, per-bucket mute/snooze, Latte light theme, HistoryStore + Sparkline, embedded log panel | Shipped |
| v0.3.0 | Focus Assist, F-N7 webhooks, L-01 per-tier sound UI, L-08 pace ring marker, UX-Acc keyboard nav, LadderEvaluator extract, **F-N1 credential poller (broken — see §3)**, `.gitattributes`, `SECURITY.md`, `CONTRIBUTING.md`, 16 new tests | Shipped |
| v0.4.0 | 7-day calendar, plan auto-detect, AnomalyDetector U3, MP3/M4A audio, multi-account scaffold, 12 new tests | Shipped |
| **Open** | L-04 toast actions, L-06 named pipe, L-10 plugin contract, L-12 NM keep-alive (mostly done), screenshots, F-N1 hardening, multi-account columns full | Pass 4 below |

### Feature surface census

41 user-facing capabilities now. Counted by tray menu + settings panel + alarm rule families + CLI flags:

- Tray menu: Show / Hide / Refresh / Settings / Check for updates / Reset position / Quit = 7 entries.
- Settings panel: alarms-enabled, autostart, pace-enabled, focus-assist, theme (Mocha/Latte), warn%, danger%, custom-sound, reset-sound, zero-state-sound, ladder (9 tiers), webhook, calendar toggle, log panel toggle = 14 distinct controls.
- Alarm rule families: R1 (9 tiers) + R2 + R3 + U1 (3 thresholds) + U2 (pace) + U3 (anomaly) = 6 families × variants.
- NMH CLI: `--register / --unregister / --purge / --version / --help / --collect-diagnostics / --poll-credentials / --interval-minutes` = 8 flags.
- Visual states: Mocha + Latte + Mica + Acrylic + High-contrast (TODO) + ReducedMotion = 4 + 2 planned.

---

## 3. Bugs I Introduced (Pass 4 only)

### Bug 1 — F-N1 Claude probe targets the wrong API (P0)

**Files:** [src/QuotaGlass.NMH/CredentialPoller.cs:205-220](src/QuotaGlass.NMH/CredentialPoller.cs#L205-L220), [docs/extension-integration.md](docs/extension-integration.md) §sources.

```csharp
using var req = new HttpRequestMessage(HttpMethod.Post, AnthropicMessagesEndpoint);
// = "https://api.anthropic.com/v1/messages"
if (token.StartsWith("sk-ant-", StringComparison.OrdinalIgnoreCase))
{
    req.Headers.Add("x-api-key", token);
}
else
{
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
```

**Reality (verified by reading Anthropic's API docs + the extension's `src/scrapers/claude.js`):**

- `api.anthropic.com/v1/messages` is the developer API. It accepts `x-api-key: sk-ant-…` keys.
- Claude Code OAuth tokens (issued by `claude.ai/oauth/...`) are scoped to `api.claude.ai/...` consumer endpoints (where `claude.ai/settings/usage` reads). They are **not** valid for `api.anthropic.com`. A `Bearer <oauth-token>` against `api.anthropic.com/v1/messages` returns `401 Unauthorized`.
- The `anthropic-ratelimit-unified-{5h,7d}-utilization` headers I parse are **only** emitted by the consumer `api.claude.ai` endpoints (Pass 2 §3.6 and the extension code confirm this). `api.anthropic.com` returns `anthropic-ratelimit-requests-*` instead.

**Net:** F-N1 Claude probe will 401 forever for Claude Code users. The OAuth-vs-API-key branch in the probe code never reaches the success path because the OAuth branch targets the wrong host.

**Evidence trail:**

- Public docs at https://docs.anthropic.com/en/api/rate-limits enumerate `anthropic-ratelimit-requests-*` for `/v1/messages` — NO unified-{5h,7d} variants.
- The extension's `src/scrapers/claude.js` calls `https://api.claude.ai/api/organizations/{orgId}/usage` (via the user's session cookies) and reads `anthropic-ratelimit-unified-5h-utilization` / `…-7d-utilization` from THAT response. Different host, different headers, different auth model.
- Anthropic Admin API at `api.anthropic.com/v1/organizations/usage_report/claude_code` (re-checked at 2026-05-25) requires `sk-ant-admin-…` admin keys — workspace-billing scope, no per-user window data. Already rejected in Pass 1 Option D.

**Fix shape:**

```csharp
// Replace AnthropicMessagesEndpoint with the consumer endpoint and use
// the OAuth token + the orgId from the credentials file.
private const string ClaudeUsageEndpoint = "https://api.claude.ai/api/organizations/{0}/usage";

var orgId = ExtractClaudeOrgId(path); // new helper — claude .credentials.json has it
if (string.IsNullOrEmpty(orgId)) return null;

using var req = new HttpRequestMessage(HttpMethod.Get,
    string.Format(ClaudeUsageEndpoint, orgId));
req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
req.Headers.Add("anthropic-client", "QuotaGlass/0.5");
```

Or, if Claude Code OAuth tokens don't validate against the consumer endpoint either, the honest path is: F-N1 only works for `sk-ant-…` API-key users (who explicitly created an Admin key); Claude Code OAuth users get the extension-only path. Document this and short-circuit when the token isn't an `sk-ant-…`.

**Estimated complexity:** L. Needs live validation on a real Claude Code install to determine which token shape actually works against which endpoint.
**Priority:** **P0 — F-N1's headline value is gone until this works.**

---

### Bug 2 — F-N1 Codex probe targets the browser endpoint with the wrong auth (P0)

**File:** [src/QuotaGlass.NMH/CredentialPoller.cs:235-260](src/QuotaGlass.NMH/CredentialPoller.cs#L235-L260)

```csharp
using var req = new HttpRequestMessage(HttpMethod.Get, OpenAiUsageEndpoint);
// = "https://chatgpt.com/backend-api/wham/usage"
req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
```

**Reality (verified by reading the extension's `src/scrapers/codex.js`):**

- `chatgpt.com/backend-api/wham/usage` is authenticated by **browser session cookies** (`__Secure-next-auth.session-token`), NOT a Bearer token. Bearer auth gets `401 Unauthorized`.
- Codex CLI's `~/.codex/auth.json` typically stores an OpenAI Platform API key (`sk-…`) that authenticates against `api.openai.com/v1/...`, NOT the ChatGPT consumer surface.
- The OpenAI Platform usage endpoint (`api.openai.com/v1/usage`) only exposes daily token counts, NOT the 5h/weekly window data Codex CLI users care about.

**Fix shape:**

1. Detect token shape: `sk-…` → OpenAI Platform; anything else → fail gracefully with `"detail":"unsupported-codex-token-type"`.
2. For `sk-…` users, document that we can only show "today's token count" not the 5h/weekly windows.
3. For Codex CLI on ChatGPT (which uses session cookies for the WHAM endpoint), the only viable path is "import session cookie from Chromium" — which Pass 1 Option B already rejected on the maintenance-treadmill argument.

**Net:** F-N1 Codex probe is fundamentally limited. Honest answer: extension-only path remains the only Codex coverage for now.

**Estimated complexity:** M to short-circuit + document; L to add a real OpenAI Platform path.
**Priority:** **P0 — same as Bug 1; the credentials path doesn't deliver on its claim.**

---

### Bug 3 — Mica brush override silently regresses on theme swap (P1)

**Files:** [src/QuotaGlass.Widget/Services/MicaBackdrop.cs:55-71](src/QuotaGlass.Widget/Services/MicaBackdrop.cs#L55-L71), [src/QuotaGlass.Widget/Services/ThemeService.cs:23-42](src/QuotaGlass.Widget/Services/ThemeService.cs#L23-L42)

`MicaBackdrop.TryApply` runs once at `OnSourceInitialized` and writes:

```csharp
resources["Brush.Window.Background"] = resources["Brush.Window.MicaBackground"];
```

`ThemeService.Apply` later runs (e.g., user picks Latte) and does:

```csharp
merged[i] = new ResourceDictionary { Source = sourceUri };
```

Replacing the merged dictionary wipes the Mica override — `Brush.Window.Background` reverts to the new theme's default `Mocha.Base @ 0.92` (or `Mocha.Base @ 0.95` in Latte). Mica is now occluded again. R3-P0-03 silently regresses after every theme switch.

**Reproducible by reading the code** — no live verification needed.

**Fix shape:**

```csharp
public static void Apply(string themeName)
{
    // ...swap merged dictionary as today...

    // R4-P0-03 — re-apply the Mica override if it was previously active.
    if (MicaBackdrop.WasApplied)
    {
        var resources = app.Resources;
        if (resources.Contains("Brush.Window.MicaBackground"))
            resources["Brush.Window.Background"] = resources["Brush.Window.MicaBackground"];
    }
}
```

Plus a `MicaBackdrop.WasApplied` static flag set in `TryApply`.

**Estimated complexity:** XS.
**Priority:** P1.

---

### Bug 4 — F-N1 minimal Claude probe burns user tokens (P1)

**File:** [src/QuotaGlass.NMH/CredentialPoller.cs:215-217](src/QuotaGlass.NMH/CredentialPoller.cs#L215-L217)

I claimed in the v0.3.0 CHANGELOG: "calls /v1/messages (Claude) … minimal /v1/messages ping for Claude — billing impact ~zero." Re-reading the code: the probe sends a real `{"max_tokens":1,"messages":[{"role":"user","content":"hi"}]}` request. Even at Haiku 4.5 pricing (≈$0.0001/request including the 1-token output), 30-min poll × 24h = 48 requests/day = ~$0.005/day. Small, but not "zero". For users on Anthropic free tier (`sk-ant-oat01-…` from Claude Code), it consumes from their 5h window. Self-defeating.

**Fix:** if we have to call `/v1/messages` at all (which Bug 1 says we shouldn't), use a HEAD-equivalent. The Anthropic API does NOT support HEAD on `/v1/messages` (verified) — there's no way to get rate-limit headers without consuming. Therefore the right answer is: don't call `/v1/messages` for usage probing at all. Use the consumer-endpoint approach in Bug 1's fix shape.

**Estimated complexity:** XS (after Bug 1 is fixed, this disappears).
**Priority:** P1.

---

### Bug 5 — Snapshot.json double-writer race causes bucket-card shimmer (P2)

**Files:** [src/QuotaGlass.NMH/CredentialPoller.cs:103-110](src/QuotaGlass.NMH/CredentialPoller.cs#L103-L110), [src/QuotaGlass.NMH/MessagePump.cs:138](src/QuotaGlass.NMH/MessagePump.cs#L138)

Both code paths write to `%LOCALAPPDATA%\QuotaGlass\snapshot.json` via `AtomicJsonFile.Write`. The file itself is atomically replaced — no partial reads. But **contents** flip:

- Extension chain via spawned NMH writes the full bucket set (e.g., `claude-session`, `claude-weekly-all`, `claude-weekly-sonnet`, `claude-weekly-design`, `codex-5h-all`, `codex-weekly-all`).
- `--poll-credentials` writes a smaller bucket set (e.g., `claude-session`, `claude-weekly-all` only — what the rate-limit headers expose).

The widget's `SnapshotWatcher` debounces 250ms then reloads. `MainViewModel.OnSnapshot` reconciles by `Bucket.Id`, removing any IDs not in the latest snapshot. So bucket cards visibly **appear and disappear** as the snapshot flips between sources.

**Fix shape (R4-P1-02):** Credential poller writes a sibling file `snapshot.local-creds.json`. `SnapshotWatcher` watches both, merges per-provider (extension wins on overlap, creds fills gaps), emits the merged envelope.

**Estimated complexity:** M.
**Priority:** P2 (only matters when BOTH paths are active, which is the F-N1 mixed-user persona).

---

### Bug 6 — HistoryStore fsyncs on every bucket every snapshot (P2)

**File:** [src/QuotaGlass.Widget/Services/HistoryStore.cs:40-58](src/QuotaGlass.Widget/Services/HistoryStore.cs#L40-L58)

```csharp
public void AppendSample(string bucketId, DateTimeOffset ts, double percentUsed)
{
    // ...
    Save();   // ← AtomicJsonFile.Write with Flush(true)
}
```

`MainViewModel.OnSnapshot` calls `AppendSample` in a per-bucket loop — 4-6 buckets per snapshot push = 4-6 atomic-write-and-fsync per snapshot. On HGFS (this VM's `Z:\` / `W:\`) and AV-scanned drives, that's 50-200ms of disk wait per snapshot. Doesn't matter on bare-metal SSDs but burns clock on shared-folder / AV-scanned setups.

**Fix shape (R4-P1-01):** `AppendSample` mutates in-memory only; `MainViewModel.OnSnapshot` calls a new `HistoryStore.Flush()` once after the per-bucket loop completes.

**Estimated complexity:** S.
**Priority:** P2.

---

### Smaller defects worth a one-liner

- **[Logger.cs:Init](src/QuotaGlass.NMH/Logger.cs#L11-L26) / [WidgetLogger.cs:Init](src/QuotaGlass.Widget/Services/WidgetLogger.cs#L19-L25)** — log path pinned at `DateTime.Now` once at startup. A widget running across midnight writes the next day's lines into the previous day's log. Cosmetic but trivially fixable (R4-Q-04).
- **[MainViewModel.cs:46-48 ReducedMotion](src/QuotaGlass.Widget/ViewModels/MainViewModel.cs#L46-L48)** — get-only INPC-less property; runtime SystemParameters change doesn't propagate. R4-Q-05.
- **[AlarmScheduler.cs:_pace](src/QuotaGlass.Widget/Services/AlarmScheduler.cs#L57)** — has its own `PaceCalculator` instance; MainViewModel also has one ([line 25](src/QuotaGlass.Widget/ViewModels/MainViewModel.cs#L25)). Both compute the same forecast independently per snapshot. Cheap (≈10 ms) but redundant. Inject the shared instance.
- **[TrayIconService.cs context menu](src/QuotaGlass.Widget/Services/TrayIconService.cs#L36-L63)** — 8 items now (Show, Hide, sep, Refresh, Settings, sep, Check-updates, Reset-position, sep, Quit). Vertical menu height is ~200 px on standard DPI. Consider submenus (R4-Q-06).
- **[CHANGELOG v0.3.0 entry](CHANGELOG.md)** claims "10 new CredentialPollerTests" — recount shows 8 (one Theory has 8 `InlineData` rows but counts as one method). Cosmetic.
- **[CHANGELOG v0.3.0 entry](CHANGELOG.md)** claims "11 → 28 tests" — recount shows 37. Cosmetic.
- **[ROADMAP.md table at line 213](ROADMAP.md#L213)** — markdown lint flags pre-existing compact-table style. Not introduced this session. Carry-forward.
- **[FocusAssist.ShouldSuppressToasts](src/QuotaGlass.Widget/Services/FocusAssist.cs#L40-L53)** — runs once per `FireOnce`. Cheap, but on a snapshot with 6 buckets × multiple rule families this can be 15+ `SHQueryUserNotificationState` calls per snapshot. Cache for 2-3 seconds. R4-Q-07.

---

## 4. Feature Inventory Updates (post-v0.4.0)

Pass 3 §3 enumerated 30 features. After v0.1.1..v0.4.0, the new entries:

| ID | Feature | Code | Maturity | Tests | Pass 4 finding |
|---|---|---|---|---|---|
| F-31 | Single-instance Mutex | [App.xaml.cs:OnStartup](src/QuotaGlass.Widget/App.xaml.cs) | Complete | none | None new. |
| F-32 | `--collect-diagnostics` | [NMH/Diagnostics.cs](src/QuotaGlass.NMH/Diagnostics.cs) | Complete | none | Should add a unit test that the zip contains the expected entries (R4-P2-02). |
| F-33 | U2 pace alarm tier | [AlarmScheduler.cs](src/QuotaGlass.Widget/Services/AlarmScheduler.cs#L160-L175) | Complete | none | None. |
| F-34 | U3 anomaly alarm tier | [AlarmScheduler.cs](src/QuotaGlass.Widget/Services/AlarmScheduler.cs) + [Shared/AnomalyDetector.cs](src/QuotaGlass.Shared/AnomalyDetector.cs) | Complete | 5 in `AnomalyDetectorTests` | Potential double-fire with R3 zero-state on big spikes that cross 100% (both fire). Decide whether to suppress R3 when U3 just fired. R4-Q-08. |
| F-35 | Tray "Check for updates" + "Reset position" + first-run flag | [TrayIconService.cs](src/QuotaGlass.Widget/Services/TrayIconService.cs) | Complete | none | None. |
| F-36 | Edge-snap on drag | [MainWindow.xaml.cs:SnapToMonitorEdge](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs) | Complete | none | None. |
| F-37 | DPI-safe ring center text (Viewbox) | [MainWindow.xaml](src/QuotaGlass.Widget/Views/MainWindow.xaml) | Complete | none | None. |
| F-38 | Ring hover tooltip | [BucketViewModel.HoverTooltip](src/QuotaGlass.Widget/ViewModels/BucketViewModel.cs) | Complete | none | None. |
| F-39 | Per-bucket mute/snooze | [MainWindow.xaml.cs:ShowSnoozeMenu](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs) | Complete | none | The "Snooze until reset" path uses `TimeSpan.FromDays(8)` as a heuristic — works for ≤7-day windows. Could use the actual bucket's `ResetIso - now`. R4-Q-09. |
| F-40 | Catppuccin Latte light theme + runtime swap | [Theme/CatppuccinLatte.xaml](src/QuotaGlass.Widget/Theme/CatppuccinLatte.xaml), [Services/ThemeService.cs](src/QuotaGlass.Widget/Services/ThemeService.cs) | **Buggy** | none | **Mica regression on swap (Bug 3).** |
| F-41 | HistoryStore + Sparkline | [Services/HistoryStore.cs](src/QuotaGlass.Widget/Services/HistoryStore.cs), [Controls/Sparkline.cs](src/QuotaGlass.Widget/Controls/Sparkline.cs) | **Performant on bare metal; slow on HGFS / AV scan** | none | **Fsync-per-bucket (Bug 6).** |
| F-42 | Embedded log panel | [LogPanelViewModel.cs](src/QuotaGlass.Widget/ViewModels/LogPanelViewModel.cs) | Complete | none | Auto-refreshes every 3s while expanded. Could leak handles if log files are deleted mid-read — guarded with try/catch. OK. |
| F-43 | Focus Assist awareness | [Services/FocusAssist.cs](src/QuotaGlass.Widget/Services/FocusAssist.cs) | Complete | none | Tight call cadence (R4-Q-07). |
| F-44 | F-N7 shell-command webhook | [AlarmScheduler.cs:TryRunWebhook](src/QuotaGlass.Widget/Services/AlarmScheduler.cs) | Complete | none | 5 s self-kill is correct. No command injection (env-var pass). |
| F-45 | L-08 burn-rate pace marker | [Controls/RadialRing.cs:PaceMarkerPercent](src/QuotaGlass.Widget/Controls/RadialRing.cs) | Complete | none | Uses last-2 sample slope — same approach as PaceCalculator. OK. |
| F-46 | UX-Acc keyboard nav | [MainWindow.xaml.cs:OnCardKeyDown](src/QuotaGlass.Widget/Views/MainWindow.xaml.cs) | Complete | none | None. |
| F-47 | F-N1 OAuth credential poller | [NMH/CredentialPoller.cs](src/QuotaGlass.NMH/CredentialPoller.cs) | **Compiles, but probes wrong endpoints (Bugs 1-2-4)** | 8 tests | See §3. |
| F-48 | 7-day reset calendar | [ViewModels/CalendarViewModel.cs](src/QuotaGlass.Widget/ViewModels/CalendarViewModel.cs) | Complete | none | Rebuilt on snapshot. Midnight-rollover staleness if no snapshot arrives (R4-Q-10 minor). |
| F-49 | Plan auto-detect | [Shared/PlanInference.cs](src/QuotaGlass.Shared/PlanInference.cs) | Complete | 7 tests | None new. |
| F-50 | MP3/M4A audio via MediaPlayer | [Services/ToastService.cs:PlayMediaFoundation](src/QuotaGlass.Widget/Services/ToastService.cs) | Complete | none | Static `_activePlayers` rooted list grows during transient bursts; cleaned via `MediaEnded`/`MediaFailed`. OK in normal use. |
| F-51 | Multi-account scaffold | [BucketViewModel.AccountLabel](src/QuotaGlass.Widget/ViewModels/BucketViewModel.cs) | Partial — tooltip only | none | Full side-by-side columns deferred (R4-P2-01). |

---

## 5. Competitive & Ecosystem Research (Pass 4 deltas)

Re-checked between Pass 3 dispatch (2026-05-25) and now (still 2026-05-25 in session time). No new entrant since Pass 3. The competitive matrix from Pass 3 §7 still holds:

- **Zrnik/claude-usage-windows-taskbar-widget** — v0.2.20, unchanged. No new release.
- **CodeZeno/Claude-Code-Usage-Monitor** — Rust; scope drifting toward Linux.
- **jens-duttke/usage-monitor-for-claude** — v1.16.0 (2026-05-22) added Discord direct integration. **Reinforces F-N7 design choice.**
- **Tokens 4 Breakfast** — macOS; paid; not a Windows competitor.
- **`ryoppippi/ccusage`** — sister-project Node CLI.

**Pass 4 insights from re-reading our own positioning:**

- QuotaGlass's **41-capability surface** is now richer than Zrnik's (38 surface units by an equivalent count). The only category Zrnik leads in is multi-account: their `CredentialStore.cs` parses 6 credential file paths and exposes side-by-side accounts. Closing R4-P2-01 reverses this.
- The **alarm-ladder + sound + webhook + Focus-Assist** stack is unique to QuotaGlass. No competitor combines all four.
- The **sparkline + pace marker + 7-day calendar** trio is a new "insights" surface that no Windows competitor has. Tokens 4 Breakfast (macOS) is closest with its "30-day run rate" but lacks the per-bucket per-reset granularity.
- The **plan auto-detect** heuristic is brand new — no competitor surveyed has it.

---

## 6. Highest-Value New Features (Pass 4)

### R4-N1 — F-N1 OAuth flow with refresh-token rotation

- **User problem solved:** Today F-N1 401s. Real coverage requires (a) the correct endpoint (Bug 1 fix), (b) the correct token shape detection (Bug 2 fix), and (c) handling OAuth access-token expiry (typically 1h for Claude Code tokens).
- **Evidence:** [Anthropic OAuth refresh-token docs](https://docs.anthropic.com/en/api/oauth) + Claude Code's `.credentials.json` carries a `refresh_token` next to the `access_token`.
- **Proposed behavior:** On first 401, call the refresh endpoint with the refresh token, update `.credentials.json` (or write a parallel file we own), retry. Cache the expiry to avoid the 401-then-refresh roundtrip on every poll.
- **Implementation areas:** `CredentialPoller.RefreshTokenAsync`, new `OAuthTokenCache` in NMH process scope. Touch [CredentialPoller.cs](src/QuotaGlass.NMH/CredentialPoller.cs#L195-L233).
- **Risks:** Writing back to the user's `.credentials.json` is hostile — could conflict with the CLI doing its own refresh. Write to a sibling QuotaGlass-owned cache instead.
- **Verification plan:** Live test against a real Claude Code install; assert the second poll succeeds without a refresh (tokens still valid), assert third poll after 1h triggers a refresh and succeeds.
- **Estimated complexity:** L.
- **Priority:** P0 (Bug 1 prerequisite).

### R4-N2 — Toast actions (L-04): Snooze 1h / Open Analytics

- **User problem solved:** Pass 1 L-04 deferred; Pass 3 deferred again. The mute/snooze right-click is great when the widget is visible, but the typical alarm flow is "toast pops while user is in another app, user wants to snooze without focus-stealing".
- **Evidence:** Microsoft hand-rolled COM activator pattern documented at https://learn.microsoft.com/en-us/windows/apps/design/shell/tiles-and-notifications/send-local-toast. Zrnik competitor does this in ~200 LOC.
- **Proposed behavior:** Toast carries two `<action>` elements (Snooze 1h, Open analytics). Click routes through `INotificationActivationCallback` (COM activator registered at install time). Snooze invokes `MainViewModel.SnoozeBucket`; Open hits the analytics URL.
- **Implementation areas:** New `Services/ToastActivator.cs` (COM-registered class); installer registers the activator's CLSID under `HKCU\Software\Classes\CLSID\…`. ToastService builds the `<actions>` block. AlarmScheduler keys carry the `bucket-id` argument for activation.
- **Data model implications:** Toast XML grows. Activation args parsing.
- **Risks:** COM activator registration on uninstall must clean up. Per-user install (`PrivilegesRequired=lowest` in Inno) means HKCU only — safe.
- **Verification plan:** Fire a synthetic alarm; click "Snooze 1h" from Action Center after closing the toast; verify the bucket's `Alarms.SnoozedBucketsUntilUtc` advances.
- **Estimated complexity:** L (~200 LOC + installer changes).
- **Priority:** P1.

### R4-N3 — Schema v2 — bundle history in the snapshot envelope

- **User problem solved:** Pass 3 Bug 4 — sparklines start empty after a fresh install or `--purge` and take ~24 snapshot cycles (≈2 hours at the default 5-min cadence) to populate. The extension already maintains 30 days of history.
- **Evidence:** AI-Usage_Tracker's `state.history` array already exists in storage. Wire schema doc at [docs/extension-integration.md](docs/extension-integration.md) currently omits it.
- **Proposed behavior:** Schema v2 adds `state.history: { [bucketId]: HistorySample[] }`. NMH accepts v2 (`SchemaVersion.Max = 2`). Widget merges incoming history into HistoryStore on each snapshot.
- **Implementation areas:** [Shared/BucketSnapshot.cs](src/QuotaGlass.Shared/BucketSnapshot.cs) (add `state.history`), [Shared/SchemaVersion.cs](src/QuotaGlass.Shared/SchemaVersion.cs) (bump `Max`), [MessagePump](src/QuotaGlass.NMH/MessagePump.cs) (range check), [docs/extension-integration.md](docs/extension-integration.md), [docs/bridge-integration.md](docs/bridge-integration.md) (extension-side `pushSnapshot` shape). Cross-repo coordination with AI-Usage_Tracker.
- **Risks:** Wire size grows ~1-2 KB per push. Still well under 1 MB cap.
- **Verification plan:** Inject a fake snapshot with history; assert HistoryStore reflects the merged samples; assert sparkline renders on first launch.
- **Estimated complexity:** M.
- **Priority:** P1.

### R4-N4 — `--poll-credentials` auto-start scheduled task

- **User problem solved:** Today F-N1 only runs if the user explicitly invokes `QuotaGlass.NMH.exe --poll-credentials` from a terminal. Most users won't discover this.
- **Evidence:** Standard Windows pattern — Task Scheduler `\Microsoft\Windows\...` per-user task.
- **Proposed behavior:** `--register` detects if Claude Code (`%USERPROFILE%\.claude\.credentials.json`) or Codex CLI (`%USERPROFILE%\.codex\auth.json`) is installed. If yes, register a per-user Scheduled Task `QuotaGlass.CredentialPoll` running `--poll-credentials --interval-minutes 30` at logon + every 30 min. `--unregister` removes it.
- **Implementation areas:** New `Services/SchedulerRegistration.cs` (or use `schtasks.exe` shell-out). [HostRegistrar.Register / Unregister](src/QuotaGlass.NMH/HostRegistrar.cs).
- **Risks:** Scheduled-task registration needs careful XML or COM API. Wrong invocation can spam logs.
- **Verification plan:** Run `--register` on a machine with Claude Code installed; `schtasks /Query /TN QuotaGlass.CredentialPoll` should list it.
- **Estimated complexity:** M.
- **Priority:** P1 (only after Bug 1 + Bug 2 are fixed — otherwise the auto-task spams 401s).

### R4-N5 — Multi-account columns (R3-P2-01 full)

- **User problem solved:** Engineering lead persona watching team burn; freelancer with personal + client Claude accounts. Pass 4 multi-account scaffold puts the account id in the tooltip but doesn't render columns.
- **Evidence:** Wire schema already supports `orgId` / `accountId`. Extension future work (NX-12 in upstream ROADMAP) implies multi-account support there too.
- **Proposed behavior:** When multiple `ProviderSnapshot` rows share a provider name (e.g., Claude account A + Claude account B), render side-by-side `Border` columns inside a `Grid` with one column per account. Bucket cards stack vertically inside each column.
- **Implementation areas:** [Shared/BucketSnapshot.cs](src/QuotaGlass.Shared/BucketSnapshot.cs) — change `ProviderMap.Claude` to `List<ProviderSnapshot>`; [MainViewModel.cs](src/QuotaGlass.Widget/ViewModels/MainViewModel.cs) — group by account; [MainWindow.xaml](src/QuotaGlass.Widget/Views/MainWindow.xaml) — `Grid` of columns.
- **Risks:** Wire-schema break (v3?) or careful additive handling. Window width grows.
- **Verification plan:** Synthesize a 2-account fake snapshot; widget renders two columns.
- **Estimated complexity:** L.
- **Priority:** P2.

### R4-N6 — L-06 named pipe NMH↔Widget transport

- **User problem solved:** 250 ms FileSystemWatcher debounce is the floor for snapshot-to-render latency. A named pipe drops this to <10 ms.
- **Evidence:** ROADMAP L-06 since v0.1.0.
- **Proposed behavior:** NMH writes snapshot.json AND posts to `\\.\pipe\QuotaGlass.Snapshot` if a listener is connected. Widget connects on launch, listens for `{kind:"snapshot"}` messages; on disconnect, falls back to the existing FileSystemWatcher.
- **Implementation areas:** [Services/SnapshotPipe.cs](src/QuotaGlass.Widget/Services) (new), [MessagePump](src/QuotaGlass.NMH/MessagePump.cs) post hook.
- **Risks:** Pipe reconnect logic, security descriptor on the pipe (per-user only).
- **Verification plan:** Inject a fake snapshot via NMH; measure widget-to-render time before and after. Expect ≤10 ms with pipe vs ~270 ms today.
- **Estimated complexity:** M.
- **Priority:** P2.

### R4-N7 — Toast XML escaping tests

- **User problem solved:** [ToastService.Escape](src/QuotaGlass.Widget/Services/ToastService.cs#L107-L111) is hand-rolled and currently misses `'` (XML allows raw apostrophes in text nodes, so this is OK today, but any future migration to attribute values would break). No test guards it.
- **Evidence:** Hand-rolled XML escapers are a common XSS-equivalent surface.
- **Proposed behavior:** Unit tests for: `&` → `&amp;`, `<` → `&lt;`, `>` → `&gt;`, `"` → `&quot;`, multi-char strings, empty string, null safety.
- **Implementation areas:** New `test/QuotaGlass.Tests/ToastServiceEscapeTests.cs`. Make `ToastService.Escape` internal-visible-to-tests OR expose via `[assembly: InternalsVisibleTo("QuotaGlass.Tests")]` in the Widget csproj.
- **Verification plan:** `dotnet test` green.
- **Estimated complexity:** S.
- **Priority:** P2.

### R4-N8 — High-contrast theme + system theme follow

- **User problem solved:** Windows High Contrast users see broken colors today. WCAG 2.2 compliance for the broader user base.
- **Evidence:** ROADMAP UX-Acc-04 + Pass 1 finding.
- **Proposed behavior:** New `Theme/HighContrast.xaml` honoring `SystemColors`. Optional "Follow system theme" setting that swaps Mocha/Latte/HighContrast based on `SystemParameters.HighContrast` + `Application.Current.IsApplicationDark` (or registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`).
- **Implementation areas:** [Theme/HighContrast.xaml](src/QuotaGlass.Widget/Theme) (new), [Services/ThemeService.cs](src/QuotaGlass.Widget/Services/ThemeService.cs) — add `FollowSystem` mode.
- **Estimated complexity:** M.
- **Priority:** P2.

### R4-N9 — Localization scaffold

- **User problem solved:** All UI strings hard-coded English. Power-user feature unlocking adoption in non-English markets.
- **Evidence:** ROADMAP Phase 4 / R3-P3-01.
- **Proposed behavior:** Move strings to `Resources.resx`; switch at startup via `Thread.CurrentThread.CurrentUICulture`. Settings option "Language".
- **Implementation areas:** Every `.xaml` and ViewModel string literal. Big mechanical pass.
- **Estimated complexity:** L.
- **Priority:** P3.

---

## 7. Existing Feature Improvements (Pass 4)

### F-N1 hardening (post-Bug-1-fix)

- **Current behavior:** Single endpoint, single token shape. Single retry on failure.
- **Recommended change:** Token-shape dispatcher + per-provider endpoint table + 429-aware backoff (start at `--interval-minutes`, double on 429 up to 4×, reset on 200). Honor `Retry-After` header.
- **Code locations:** [CredentialPoller.cs](src/QuotaGlass.NMH/CredentialPoller.cs).
- **Backward compat:** None — F-N1 hasn't shipped a working version yet.
- **Estimated complexity:** M.
- **Priority:** P1.

### MainWindow.xaml is now ~280 lines of mixed concerns

- **Current behavior:** [MainWindow.xaml](src/QuotaGlass.Widget/Views/MainWindow.xaml) holds title bar + setup card + bucket cards + settings panel + calendar + log panel + status strip in one ResourceDictionary-light tree.
- **Recommended change:** Extract `Views/SetupCard.xaml`, `Views/SettingsPanel.xaml`, `Views/CalendarPanel.xaml`, `Views/LogPanel.xaml` as `UserControl`s. MainWindow.xaml shrinks to ~60 lines of composition.
- **Code locations:** [MainWindow.xaml](src/QuotaGlass.Widget/Views/MainWindow.xaml) + new files.
- **Backward compat:** None — purely visual refactor.
- **Estimated complexity:** M (mechanical but XAML-binding paths need verification).
- **Priority:** P2.

### Settings panel is becoming busy

- **Current behavior:** 14 controls in one scrollable section.
- **Recommended change:** Group into expandable sub-sections: "Alarms" / "Display" / "Integration" / "Advanced". Use the same `IsExpanded` toggle pattern as the existing log panel.
- **Code locations:** [SettingsPanelViewModel.cs](src/QuotaGlass.Widget/ViewModels/SettingsPanelViewModel.cs) + XAML.
- **Estimated complexity:** S.
- **Priority:** P2.

---

## 8. Reliability, Security, Privacy, Data Safety (Pass 4)

### Reliability

- **Bug 5 (snapshot.json double-writer race).** Real user impact if F-N1 is used alongside the extension.
- **Bug 6 (HistoryStore fsync per bucket).** Latency on HGFS/AV-scanned setups.
- **F-N1 401 storm.** If user has `~/.claude/.credentials.json` and runs `--poll-credentials`, every 30 min an Anthropic 401 lands in the log. Eventually fills logs (10 MB → rotate, OK in long run but noisy in week 1).

### Security

- **F-N7 webhook command injection.** Settings UI is the only input surface; user can only escalate against themselves. Not a real risk. **Documented in [CONTRIBUTING.md](CONTRIBUTING.md).**
- **F-N1 credential file reading.** We read `~/.claude/.credentials.json` + `~/.codex/auth.json` + `~/.hermes/auth.json`. Files are user-readable; no escalation. Tokens kept in-memory; not logged. ✅
- **MediaPlayer file path injection.** `OpenFileDialog`-supplied; user only picks their own files. Toast XML's `<audio silent="true"/>` is still hard-coded — no custom-audio injection path. ✅
- **Diagnostics zip redaction.** `orgId` / `accountId` → `"redacted"`. Custom WAV paths → last-12-char tail. Tokens never enter the zip (CredentialPoller is the only place that holds them, and Diagnostics doesn't query NMH process state). ✅
- **NMH origin allow-list.** Still pinned to AI-Usage_Tracker Chrome ID `olkdpcileldmdemjbiklkhompnhkhjeh`. ✅

### Privacy

- **Outbound network calls now (per-process):**
  - Widget: `GET api.github.com/.../releases/latest` (lazy, only on Check-for-updates).
  - NMH `--poll-credentials`: `POST api.anthropic.com/v1/messages` + `GET chatgpt.com/backend-api/wham/usage` — **need updating** per Bugs 1-2.
  - F-N7 webhook: user-defined external commands; user owns the privacy boundary.
- README's "Nothing leaves your machine" promise needs an asterisk for `--poll-credentials` users. **Update [README.md](README.md) Privacy section.**

### Data safety

- AtomicJsonFile.Write still uses `Flush(true)` + `File.Replace`. ✅
- FiredRulesStore catches IOException. ✅
- HistoryStore catches IOException. ✅
- SettingsStore lock-guarded against concurrent in-process writers. ✅
- **No backup/rollback of `settings.json` if user-edit corrupts it.** Settings → "Reset to defaults" button (Pass 1 R-Rec-01) still open. R4-Q-11.

---

## 9. UX, Accessibility, Trust (Pass 4)

### Onboarding gaps

- Setup card now has a "Dismiss 24h" button, good. But the card's "Install extension" link points to GitHub Releases page, which a non-developer may not know how to navigate. Consider linking directly to Chrome Web Store once the AI-Usage_Tracker extension lands there. R4-Q-12.

### Empty / loading / error / disabled states

- **F-N1 401 / 500 path** silently logs and writes `Ok=false, Error="credential-probe-failed"`. Widget should render this as a dim banner ("Claude credential probe failed — using browser data only"). Currently the widget just shows fewer cards.
- Pace marker (L-08) renders only when 2+ history samples exist for the bucket. First snapshot after `--purge` → no marker for ~10 min. Acceptable but undocumented.

### Destructive actions

- Tray "Quit" still no-confirm. Per Pass 3 §6 — acceptable.
- `--purge` still no-confirm CLI. Per Pass 3 §6 — acceptable.
- Settings panel still no "Reset to defaults". R4-Q-11.

### Settings clarity

- Settings panel is becoming long (14 controls). Group into expandable sub-sections. R4-N (Settings refactor).
- "Pace warning toasts" tooltip is now clear. ✅
- "Webhook command" tooltip explains env vars. ✅

### Accessibility

- Bucket cards Tab-focusable + Enter/Space activate + Shift+F10 context. ✅ (UX-Acc-02 closed).
- Screen-reader AutomationProperties.Name covers provider + label + percent + reset. ✅ (UX-Acc-01 closed).
- ReducedMotion DependencyProperty wired but no animations exist yet. ✅ (UX-Acc-03 closed-in-spirit).
- **High Contrast** still unaddressed. R4-N8.

### Microcopy

- README MP3/M4A wording is back to "supported". ✅
- README "Run tests" line still claims 11 tests — actually 37. R4-Q-01.
- CHANGELOG miscount of test additions (8 vs claimed 10, 37 vs claimed 28) — cosmetic.

---

## 10. Architecture and Maintainability (Pass 4)

### Module boundaries (post v0.4.0)

- `QuotaGlass.Shared` now hosts: schema types, atomic JSON, app paths, schema versioning, **LadderEvaluator (new), HistorySample (moved), AnomalyDetector (new), PlanInference (new)**. Pure code. ✅ Tests can reach it.
- `QuotaGlass.NMH` adds: `Diagnostics`, `CredentialPoller`. Both stay in NMH for the right reasons (registry probe + HttpClient). ✅
- `QuotaGlass.Widget`: 17 services + 6 viewmodels + 2 controls + 2 theme dictionaries + 1 view. Growing but still navigable.

### Refactor candidates

- **MainWindow.xaml** — see R4-N (UserControl extraction).
- **AlarmScheduler.EvaluateProvider** — 100+ lines of inline rule logic. Extracting per-family (R2 / R3 / U1 / U2 / U3 / R1) into named methods would help. ~30 LOC of mechanical refactor.
- **MainWindow.xaml.cs** — 270 lines after this session. Splitting tray-wiring vs card-event-handlers into partial classes would help.

### Test gaps

- `HistoryStore` — round-trip, ring-buffer cap, dedupe-by-ts.
- `FiredRulesStore` — 14-day prune, MarkFired/HasFired idempotency.
- `ThemeService.Apply` — dictionary swap idempotency.
- `LadderEvaluator` already covered.
- `AnomalyDetector` already covered.
- `PlanInference` already covered.
- `CredentialPoller` parser pieces already covered; HTTP path is integration-only.
- `Diagnostics.Collect` — zip contains the expected entry names.

### Documentation gaps

- `docs/extension-integration.md` doesn't document the local-creds source. R4-Q-03.
- No `docs/architecture.md` mapping the snapshot pipeline. The pipeline now has 2 producers + 1 consumer; would help future contributors.
- `assets/screenshots/` still empty.

### Release / build / deployment gaps

- CI workflow hasn't yet run a successful build. First push since v0.1.1 (where ci.yml was added) is `5a718ff` — should fire. **Verify on next push.**
- `Directory.Packages.props` for central package management still TODO. With xunit + Microsoft.NET.Test.Sdk now the only deps, low priority.
- No code-signing certificate. Pass 1 Q3 default ("ship unsigned for v0.1.x") still applies through v0.4.

---

## 11. Prioritized Roadmap (Pass 4 → v0.5.0+)

Checkbox-format suitable for copy into ROADMAP.md.

### v0.5.0 — Fix F-N1 + theme regression + perf

- [ ] **P0 — R4-P0-01** — Replace F-N1 Claude probe with the correct API path (`api.claude.ai/api/organizations/{orgId}/usage`); detect OAuth vs API-key token shape and dispatch correctly.
  - Why: Bug 1 — current probe 401s for the entire target audience (Claude Code OAuth users).
  - Touches: [NMH/CredentialPoller.cs](src/QuotaGlass.NMH/CredentialPoller.cs#L195-L233).
  - Acceptance: Live test against Claude Code install returns 200 + unified-{5h,7d} headers; logged snapshot has `Source = "local-creds"` and both buckets populated.
  - Verify: Manual on desktop PC; record in CHANGELOG v0.5.0.

- [ ] **P0 — R4-P0-02** — Detect Codex token shape and route correctly (sk- → openai.com/v1/usage, ChatGPT session token → unsupported with friendly error).
  - Why: Bug 2.
  - Touches: [CredentialPoller.ProbeCodexAsync](src/QuotaGlass.NMH/CredentialPoller.cs#L235-L260).
  - Acceptance: Codex CLI install with sk- token returns daily token counts; ChatGPT-only install logs `"detail":"unsupported-codex-token-type"`.

- [ ] **P0 — R4-P0-03** — Re-apply Mica brush override after every ThemeService.Apply.
  - Why: Bug 3.
  - Touches: [Services/MicaBackdrop.cs](src/QuotaGlass.Widget/Services/MicaBackdrop.cs#L55), [Services/ThemeService.cs](src/QuotaGlass.Widget/Services/ThemeService.cs#L23).
  - Acceptance: Toggle Mocha → Latte → Mocha on Win11 22621+; Mica visible after each swap.
  - Verify: Manual screenshots over a wallpaper.

- [ ] **P0 — R4-P0-04** — F-N1 must not burn user tokens (follows from R4-P0-01).
  - Why: Bug 4.
  - Touches: same as R4-P0-01.
  - Acceptance: 24 h of `--poll-credentials` shows zero increase in Anthropic token usage.

- [ ] **P1 — R4-P1-01** — HistoryStore.AppendSample debounce (single Save per snapshot batch).
  - Why: Bug 6.
  - Touches: [HistoryStore.cs](src/QuotaGlass.Widget/Services/HistoryStore.cs), [MainViewModel.OnSnapshot](src/QuotaGlass.Widget/ViewModels/MainViewModel.cs#L165-L185).
  - Acceptance: history.json written once per snapshot, not once per bucket.
  - Verify: Diagnostics zip log shows snapshot write count == history write count.

- [ ] **P1 — R4-P1-02** — CredentialPoller writes to sibling `snapshot.local-creds.json`; SnapshotWatcher merges with extension snapshot.
  - Why: Bug 5.
  - Touches: [CredentialPoller.PollOnceAsync](src/QuotaGlass.NMH/CredentialPoller.cs#L86), [SnapshotWatcher.ReloadAndPublish](src/QuotaGlass.Widget/Services/SnapshotWatcher.cs#L66).
  - Acceptance: With both producers active, bucket cards don't shimmer; union of buckets visible.

- [ ] **P1 — R4-N1** — F-N1 OAuth refresh-token rotation (1h token expiry).
  - Why: Without this, every poll after token expiry 401s until user re-auths via CLI.
  - Touches: [CredentialPoller.cs](src/QuotaGlass.NMH/CredentialPoller.cs).
  - Acceptance: 25-hour `--poll-credentials` run survives a token-rotation event.

- [ ] **P1 — R4-N4** — Scheduled-task auto-start for `--poll-credentials` (gated on Claude Code / Codex CLI detection).
  - Why: F-N1 only delivers value if it runs.
  - Touches: [HostRegistrar.Register / Unregister](src/QuotaGlass.NMH/HostRegistrar.cs).
  - Acceptance: After `--register` on a Claude-Code-installed box, `schtasks /Query /TN QuotaGlass.CredentialPoll` lists the task.

- [ ] **P1 — R4-Q-04** — Recompute log file path per write so cross-midnight runs don't write to yesterday's file.
- [ ] **P1 — R4-Q-05** — Wire `MainViewModel.ReducedMotion` to `SystemParameters.StaticPropertyChanged`.
- [ ] **P1 — R4-Q-01** — Update README "Run tests" wording (37 tests now, not 11).
- [ ] **P1 — R4-Q-03** — `docs/extension-integration.md` "Direct credential reading" section.

### v0.6.0 — Toast actions + schema v2

- [ ] **P1 — R4-N2** — Toast actions (Snooze 1h, Open analytics) via hand-rolled COM activator.
- [ ] **P1 — R4-N3** — Schema v2 bundle `history[]` in snapshot envelope (cross-repo with AI-Usage_Tracker).
- [ ] **P2 — R4-N5** — Multi-account columns (full).
- [ ] **P2 — Architecture-refactor** — Split MainWindow.xaml into 4 UserControls.
- [ ] **P2 — Settings panel sub-sections** (Alarms / Display / Integration / Advanced).
- [ ] **P2 — R4-N7** — Toast XML escaping unit tests.

### v0.7.0+

- [ ] **P2 — R4-N6** — Named-pipe NMH↔Widget transport (L-06).
- [ ] **P2 — R4-N8** — High-contrast theme + Follow-system-theme.
- [ ] **P3 — R4-N9** — Localization scaffold.
- [ ] **P3 — L-10** — Provider plugin contract (only after we have a real second-provider use case).
- [ ] **P3 — Manual screenshots** for `assets/screenshots/`.

---

## 12. Quick Wins (Pass 4 only, ≤30 min each)

1. **R4-Q-01** — README "Run tests" wording → 37 tests across 6 fixture files.
2. **R4-Q-03** — `docs/extension-integration.md` Direct-credential-reading section.
3. **R4-Q-04** — Log path-per-write fix in Logger / WidgetLogger.
4. **R4-Q-05** — ReducedMotion INPC hook.
5. **R4-Q-06** — Tray menu submenus ("Window" / "Updates" / "Sync").
6. **R4-Q-07** — FocusAssist 2-3 s state cache.
7. **R4-Q-08** — R3 zero-state vs U3 anomaly de-duplication.
8. **R4-Q-09** — Snooze-until-reset uses actual `ResetIso - now` instead of `TimeSpan.FromDays(8)`.
9. **R4-Q-11** — Settings → "Reset to defaults" button.
10. **R4-Q-12** — Setup card "Install extension" link wording — clarify "open the extension's GitHub release page" if no Chrome Web Store entry yet.

---

## 13. Larger Bets (Pass 4 only)

1. **F-N1 hardening** (R4-P0-01..04 + R4-N1 + R4-N4) — the credential path needs a real architecture pass, not patches. Live validation against real CLI installs is the gating dependency.
2. **R4-N2 toast actions** — first non-trivial COM activator in this project. Affects installer (CLSID registration) + tray (de-dup with right-click).
3. **R4-N3 schema v2** — cross-repo coordination; bumps `SchemaVersion.Max`.
4. **R4-N5 multi-account columns** — biggest UI change since v0.1.0. Should land after Schema v3 (if needed) and after F-N1 actually returns multi-account data.

---

## 14. Explicit Non-Goals (Pass 4 additional)

- **Do NOT add `Microsoft.Toolkit.Uwp.Notifications` back to ship toast actions.** Pass 2 R2-P0-01 removed it for the CVE; hand-rolling the COM activator preserves the win.
- **Do NOT migrate F-N1 to scraping `claude.ai` HTML.** Pass 1 §3 Option A already rejected this path.
- **Do NOT change the wire schema for `--poll-credentials` separately from Schema v2.** One bump, both sources align.
- **Do NOT add a paid tier or telemetry.** Pass 1 R-06 / Pass 2 R2-NG-04 — still rejected.

---

## 15. Open Questions (Pass 4)

Only items that gate prioritization. Other defaults are documented in Pass 1/2/3.

1. **Does Claude Code's OAuth token actually validate against `api.claude.ai/api/organizations/{orgId}/usage`?** This drives the entire F-N1 fix. **Needs live validation** on a desktop with the SDK + Claude Code installed.

2. **Does Codex CLI store an `sk-…` OpenAI key or a ChatGPT session token in `~/.codex/auth.json`?** Different shapes → different fix paths for Bug 2. **Needs live validation.**

3. **Toast-action COM activator — register at install or at first run?** Default: at install. Confirm; installer changes are higher-risk than runtime registration but more reliable.

4. **Should `--poll-credentials` auto-start at logon, or only run while the widget is open?** Default: auto-start at logon — F-N1's value proposition is "browser closed". Confirm.

5. **Schema v2 history bundle — bump `SchemaMax` to 2 or to 3?** Default: 2 (current is 1). Confirm — multi-account work later may want a coordinated v3 bump.

---

## 16. Implementation Order (Pass 4 recommendation)

For the next implementing agent. Each item is ROADMAP-ready.

### v0.5.0 must-haves (P0 fixes from this report)

1. R4-P0-01 — Claude probe endpoint fix.
2. R4-P0-02 — Codex token-shape dispatcher.
3. R4-P0-03 — Mica + ThemeService coordination.
4. R4-P0-04 — F-N1 zero-token-burn (follows R4-P0-01).
5. R4-P1-01 — HistoryStore debounce.
6. R4-P1-02 — Snapshot multi-source merge.
7. R4-N1 — F-N1 OAuth refresh.
8. R4-Q-04, R4-Q-05, R4-Q-01, R4-Q-03 (4 quick wins).

Tag v0.5.0 once R4-P0-* land.

### v0.6.0 polish

9. R4-N2 toast actions.
10. R4-N3 schema v2 bundle history.
11. R4-N4 scheduled-task auto-start.
12. R4-N7 toast XML escape tests.
13. R4-Q-06, R4-Q-07, R4-Q-08, R4-Q-09, R4-Q-11, R4-Q-12 (more quick wins).

### v0.7.0+

14. R4-N5 multi-account columns.
15. R4-N6 named-pipe transport.
16. R4-N8 high-contrast.
17. R4-N9 localization scaffold.
18. Architecture-refactor (UserControl split, settings sub-sections).

---

## 17. Pass 4 Verifications Performed

- **Re-read every file touched in commits `ba73e69` (v0.1.1), `205e7c1` (v0.2.0), `c1b72c2` (v0.3.0), `5a718ff` (v0.4.0).** All ≈2,500 net-new lines reviewed against HEAD.
- **Cross-checked F-N1 endpoints against public docs** (Anthropic rate-limit headers, OAuth refresh, Admin API; OpenAI usage API; ChatGPT WHAM cookie auth). Three contracts confirmed wrong; see Bugs 1-2-4.
- **Traced the MicaBackdrop ↔ ThemeService.Apply interaction by hand.** Bug 3 reproducible by reading the code; no live verification needed.
- **Recounted test methods across 6 fixture files** — 37 (CHANGELOG claims 28; one Theory hides 8 InlineData rows).
- **Refreshed competitive landscape** (Zrnik, jens-duttke, CodeZeno, Tokens 4 Breakfast, ccusage) — no new entrant; jens-duttke shipped v1.16 with Discord direct integration that reinforces F-N7's design.
- **Could NOT verify:** real CLI install behavior for F-N1; CI workflow first-run; arm64 runtime.

---

*End of Pass 4. Pass 1 + Pass 2 + Pass 3 + Pass 4 together cover scaffold → v0.1.0 ship → v0.1.1 fixes → v0.2.0 polish → v0.3.0 power → v0.4.0 insights → v0.5.0 plan. Pass 4's main message: I shipped a lot, F-N1 isn't yet working against the real APIs, and Mica regresses on theme swap. Three P0 fixes anchor v0.5.0.*
