# Extension Integration Contract

**Schema version:** 1
**Last updated:** 2026-05-25
**Stability:** Pre-release. Breaking changes pre v0.1.0; semver-stable after.

This document is the canonical contract between:

- **Producer:** the AI-Usage_Tracker browser extension (`~/repos/AI-Usage_Tracker`), specifically `src/lib/bridge.js` (added by F-A4).
- **Consumer:** the QuotaGlass native messaging host (`~/repos/QuotaGlass/src/QuotaGlass.NMH`), which persists each accepted snapshot to `%LOCALAPPDATA%\QuotaGlass\snapshot.json`.

Snapshot.json is then read by the QuotaGlass widget via `Services/SnapshotWatcher.cs`.

The reference C# types live in `src/QuotaGlass.Shared/BucketSnapshot.cs`.

---

## Wire format

Native messaging frames (per [Chrome's documented protocol](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging)):

```
[ 4 bytes — little-endian frame length ][ N bytes — UTF-8 JSON payload ]
```

- Max frame: 1 MB extension → host; 4 GB host → extension. We cap inbound at 1 MB.
- NMH always replies with one ack frame per inbound frame.

---

## Inbound messages (extension → NMH)

### `kind: "snapshot"` — bucket-state push

Sent on each successful refresh tick from the extension. Mirrors the extension's `state` envelope from `AI-Usage_Tracker/src/lib/storage.js#defaultState`.

```jsonc
{
  "kind": "snapshot",
  "schemaVersion": 1,
  "ts": "2026-05-25T01:23:45.678Z",
  "extensionVersion": "0.1.6",
  "state": {
    "fetchedAtISO": "2026-05-25T01:23:30.000Z",
    "providers": {
      "claude": {
        "ok": true,
        "provider": "claude",
        "source": "api",            // "api" | "stream" | "headers" | "live" | "html"
        "orgId": "abcd-1234-...",   // claude only
        "plan": "max-20x",          // nullable
        "buckets": [
          {
            "id": "claude-session",                   // stable identifier; widget reconciles by this
            "kind": "session",                        // "session" | "5h" | "weekly"
            "model": "all",                           // "all" | "sonnet" | "design" | ...
            "label": "Claude 5h session",
            "percentUsed": 64.2,                      // 0..100
            "resetISO": "2026-05-25T06:00:00.000Z",   // nullable; ISO 8601
            "rawResetText": "Resets 1:00 AM"          // nullable fallback
          },
          { "id": "claude-weekly-all", "kind": "weekly", "model": "all", "label": "Claude weekly (all)", "percentUsed": 87.5, "resetISO": "2026-05-29T05:00:00.000Z", "rawResetText": "Resets Friday 1:00 AM" }
        ]
      },
      "codex": {
        "ok": true,
        "provider": "codex",
        "source": "api",
        "accountId": "user-...",
        "plan": "plus",
        "buckets": [
          { "id": "codex-5h-all",    "kind": "5h",     "model": "all", "label": "5 hour usage limit", "percentUsed": 23.1, "resetISO": "...", "rawResetText": null },
          { "id": "codex-weekly-all","kind": "weekly", "model": "all", "label": "Weekly usage limit", "percentUsed": 91.0, "resetISO": "...", "rawResetText": null }
        ]
      }
    }
  }
}
```

Notes:

- A provider may be present with `ok: false` and an `error` field (e.g. `"usage-http-403"`). NMH still accepts the snapshot; widget surfaces the error in the corresponding provider section.
- Field names are **camelCase**; the extension's `resetISO` keeps the original casing (matches `AI-Usage_Tracker/src/lib/storage.js`).
- `extensionVersion` is informational only; NMH does not gate on it.
- `schemaVersion` is consumed by NMH for migration logic; current range = `[1, 1]`.

### `kind: "ping"` — keepalive

Sent by the extension every 25 seconds to defeat Chrome MV3's 30s service-worker idle timer.

```jsonc
{ "kind": "ping", "ts": "2026-05-25T01:23:45.000Z" }
```

NMH replies `{ ok: true, kind: "pong" }` (in the standard ack envelope; see below).

---

## Outbound messages (NMH → extension)

Every inbound frame gets exactly one ack frame.

```jsonc
{
  "ok": true,                       // false on failure
  "detail": "ok",                   // human-readable status / failure reason
  "kind": "pong",                   // only for ping replies
  "nmhVersion": "0.1.0",
  "schemaMin": 1,
  "schemaMax": 1,
  "serverTime": "2026-05-25T01:23:45.123Z"
}
```

`detail` values:

| value | meaning |
|---|---|
| `ok` | snapshot persisted |
| `pong` | ping accepted |
| `origin-rejected` | caller origin not in NMH allow-list |
| `unknown-kind` | message `kind` not recognized |
| `null-snapshot` | snapshot decoded to null |
| `json-decode-failed` | JSON parser error |
| `max-depth-exceeded` | JSON depth > 16 |
| `write-failed` | atomic write to snapshot.json failed |

---

## Color and threshold conventions

Pass 1 of the research dossier conflated two different threshold sets. Make sure to keep them separate:

- **Visual ring color ramp** (used by `Controls/RadialRing.cs` `OnRender`): **green < 60 < amber < 85 < red.** Chosen to match Catppuccin Peach (`#FAB387`) and Red (`#F38BA8`) perceptual transition. The extension itself uses 50/80 in `AI-Usage_Tracker/src/lib/countdown.js#ringColor`; both are defensible, but QuotaGlass picks 60/85 for stronger green dominance.
- **Notification rule thresholds** (used by `Services/AlarmScheduler.cs` when implemented): **75 / 90 / 95** for U1-* "approaching limit" warnings. Matches the extension's `AI-Usage_Tracker/src/lib/storage.js#defaultSettings.notifications` defaults AND Zrnik's competitor implementation.
- **Reset-imminent ladder** (R1 tiers): default `24h / 12h / 6h / 3h / 1h / 30m / 15m / 5m / at-reset`. Each tier toggleable in settings.
- **Zero-state (R3):** fires once when any bucket flips to `percentUsed >= 100` for the current `resetISO`.
- **Renewal-arrived (R2):** fires once when a bucket's `resetISO` advances AND the prior `percentUsed` was > 0 (i.e., not just a fresh first-snapshot).

---

## Identifier reference (canonical bucket IDs)

The extension's scrapers emit these stable `id` values. The QuotaGlass widget MUST reconcile by `id`, not `label`:

| id | provider | kind | model | source file |
|---|---|---|---|---|
| `claude-session` | claude | session | all | `scrapers/claude.js` |
| `claude-weekly-all` | claude | weekly | all | `scrapers/claude.js` |
| `claude-weekly-sonnet` | claude | weekly | sonnet | `scrapers/claude.js` |
| `claude-weekly-design` | claude | weekly | design | `scrapers/claude.js` |
| `claude-weekly-<model>` | claude | weekly | varies | `scrapers/claude.js` (per-model expansion) |
| `codex-5h-all` | codex | 5h | all | `scrapers/codex.js` |
| `codex-weekly-all` | codex | weekly | all | `scrapers/codex.js` |
| `codex-5h-<model>` | codex | 5h | varies | `scrapers/codex.js` |
| `codex-weekly-<model>` | codex | weekly | varies | `scrapers/codex.js` |

---

## Schema versioning policy

- Current: `1`. NMH supports `[1, 1]` (see `HostMetadata.SchemaMin/Max`).
- New fields are added as optional; consumers ignore unknown fields.
- Breaking changes bump `schemaMax`. NMH announces support range in every ack.
- When the extension's `schemaVersion` is below `NMH.schemaMin`, extension should either upgrade or NMH rejects with `"schema-too-old"`.
- When `schemaVersion` is above `NMH.schemaMax`, NMH attempts best-effort decode but logs a warning and announces drift in the ack `detail`.

---

## Direct credential reading (`--poll-credentials`)

QuotaGlass v0.3.0+ ships an alternate snapshot producer: `QuotaGlass.NMH.exe --poll-credentials` runs as a long-lived process that reads OAuth tokens from Claude Code / Codex CLI / Hermes credential files, calls the relevant per-provider usage endpoint, and writes a synthesized `SnapshotMessage` to a sibling path that the widget merges with the extension-driven snapshot.

### Token routing (v0.5+)

| Credential file | Token shape | Endpoint | Notes |
|---|---|---|---|
| `~/.claude/.credentials.json` (Claude Code) | OAuth bearer | `GET https://api.claude.ai/api/organizations/{orgId}/usage` | Parses `anthropic-ratelimit-unified-{5h,7d}-utilization` from response headers. Same path the extension scrapes via session cookies — token-auth equivalent. R4-N1: refresh-token rotation on 401. |
| `~/.hermes/auth.json` (Hermes orchestrator) | OAuth bearer | Same as above | Same Anthropic surface. |
| `~/.claude/.credentials.json` carrying `sk-ant-…` | Anthropic API key | unsupported | Admin API is workspace-billing scope only; doesn't expose per-window data. Logged with `detail="unsupported-token-type"`. |
| `~/.codex/auth.json` carrying `sk-…` | OpenAI API key | `GET https://api.openai.com/v1/usage?date=…` | Daily token count only; 5h / weekly windows are not exposed by the OpenAI Platform API. Bucket id `codex-platform-daily`. |
| `~/.codex/auth.json` carrying ChatGPT session token | Browser session token | unsupported | Cookie-auth only; QuotaGlass does not scrape Chromium cookies (Pass 1 §3 Option B rejected). |

### Output file

The credential poller writes to `%LOCALAPPDATA%\QuotaGlass\snapshot.local-creds.json`, **not** the canonical `snapshot.json`. The widget's `SnapshotWatcher` watches both files and merges per-provider — extension data wins on overlap (richer per-model buckets), credential data fills gaps (Codex when browser is closed, etc.).

### Scheduled-task auto-start

`QuotaGlass.NMH.exe --register` (v0.5+) detects any of the three credential files and registers a per-user Scheduled Task `QuotaGlass.CredentialPoll` that runs at logon + every 30 min. `--unregister` removes it. The Scheduled Task XML uses Task Scheduler 1.2 schema (stable since Win7) via `schtasks.exe` shell-out — no NuGet dep.

### CLI usage

```
QuotaGlass.NMH.exe --poll-credentials                       # default 30-min interval
QuotaGlass.NMH.exe --poll-credentials --interval-minutes 15 # override interval (clamped 5..1440)
```

---

## Idempotency keys (notifications)

Fire-once notification keys are constructed identically in extension and widget:

```
<provider>-<bucket.id>-<ruleId>-<bucket.resetISO>
```

Examples:

- `claude-claude-session-R1-1h-2026-05-25T06:00:00.000Z`
- `codex-codex-weekly-all-R3-2026-05-29T05:00:00.000Z`

The widget persists fired keys to `%LOCALAPPDATA%\QuotaGlass\settings.json` `firedRules.<key> = epoch_ms`. Pruning: drop entries older than 14 days.
