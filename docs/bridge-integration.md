# Bridge Integration — drop-in for AI-Usage_Tracker

**Status:** pending merge to `SysAdminDoc/AI-Usage_Tracker`.
**Why this lives here:** the upstream repo had ~20 files of in-progress work when this code was authored, so writing into it would risk a merge conflict. Drop these files in after the upstream branch lands.

Closes QuotaGlass roadmap items **F-A2** (pin Chrome ID) and **F-A4** (persistent-port bridge with reconnect + ping).

---

## Step 1 — pin the extension's Chrome ID (F-A2)

Generate a stable RSA-2048 keypair and embed the public key in the manifest. The deterministic Chrome ID derives from a SHA-256 of the public key's DER bytes. **One-time setup**; never regenerate without re-publishing because the ID will change.

```bash
# Generate the keypair (keep selfhost.pem out of git):
openssl genrsa -out selfhost.pem 2048
openssl rsa -in selfhost.pem -pubout -outform DER 2>/dev/null \
  | base64 -w 0 > selfhost.pubkey.b64
```

Add the base64 public key to `manifests/chrome.json` (top-level, before `"icons"`):

```jsonc
{
  "manifest_version": 3,
  "name": "AI Usage Tracker",
  ...
  "key": "<paste contents of selfhost.pubkey.b64 here>",
  ...
}
```

To compute the resulting Chrome ID (so it can be hardcoded in QuotaGlass):

```bash
openssl rsa -in selfhost.pem -pubout -outform DER 2>/dev/null \
  | openssl dgst -sha256 -binary \
  | head -c 16 \
  | xxd -p \
  | tr '0-9a-f' 'a-p'
```

Take that 32-char string and update `QuotaGlass/src/QuotaGlass.NMH/AllowedOrigins.cs`:

```csharp
public const string AiUsageTrackerChromeId = "<the 32-char ID>";
```

After this change, every developer who loads-unpacked the extension gets the same ID, regardless of install path. The NMH `allowed_origins` array (which can't wildcard per Chrome's documented protocol) now matches every install.

---

## Step 2 — add `"nativeMessaging"` permission to both manifests (F-A4)

`manifests/chrome.json` `"permissions"` array:

```diff
   "permissions": [
     "storage",
     "alarms",
     "notifications",
-    "tabs"
+    "tabs",
+    "nativeMessaging"
   ],
```

`manifests/firefox.json` — same edit.

---

## Step 3 — drop in `src/lib/bridge.js`

Pure ES module, no dependencies. Mirrors the schema documented in `QuotaGlass/docs/extension-integration.md`:

```js
// Native-messaging bridge to QuotaGlass. Sends the latest extension
// state to QuotaGlass.NMH so the Windows desktop widget can render it.
//
// Design constraints:
//  - Chrome MV3 service workers die after 30s idle. connectNative() is
//    supposed to provide a "strong keep-alive" — but per chromium issue
//    #2688 and the 2026-01 claude-code#16350 incident, the keepalive can
//    fail in practice. We send a 25s ping to defeat the timer regardless.
//  - The port can also disconnect because the native host crashes, the
//    user uninstalled QuotaGlass, or anti-virus killed the host EXE. We
//    must reconnect lazily on next push, not panic.
//  - Disconnection in the runtime.lastError path must NEVER throw
//    asynchronously — that would crash the background service worker.

const HOST_NAME = 'com.sysadmindoc.quotaglass';
const PING_INTERVAL_MS = 25_000;
const SCHEMA_VERSION = 1;

let port = null;
let pingHandle = null;

function ensurePort() {
  if (port) return port;
  try {
    port = chrome.runtime.connectNative(HOST_NAME);
  } catch (e) {
    console.warn('[QG] connectNative failed', e);
    port = null;
    return null;
  }

  port.onDisconnect.addListener(() => {
    // chrome.runtime.lastError may be set; reading it suppresses the
    // unchecked-error warning.
    if (chrome.runtime.lastError) {
      console.info('[QG] NMH disconnected:', chrome.runtime.lastError.message);
    }
    port = null;
    stopPing();
  });

  port.onMessage.addListener((msg) => {
    // Useful during development; quiet in production.
    if (msg && msg.ok === false) {
      console.warn('[QG] NMH rejected:', msg.detail);
    }
  });

  startPing();
  return port;
}

function startPing() {
  stopPing();
  pingHandle = setInterval(() => {
    try {
      ensurePort()?.postMessage({ kind: 'ping', ts: new Date().toISOString() });
    } catch (e) {
      console.warn('[QG] ping failed', e);
      port = null;
    }
  }, PING_INTERVAL_MS);
}

function stopPing() {
  if (pingHandle) {
    clearInterval(pingHandle);
    pingHandle = null;
  }
}

/**
 * Push the latest extension state to QuotaGlass.
 * Called from background.js after each successful mergeSnapshot.
 */
export function pushSnapshot(state, extensionVersion) {
  try {
    const p = ensurePort();
    if (!p) return;
    p.postMessage({
      kind: 'snapshot',
      schemaVersion: SCHEMA_VERSION,
      ts: new Date().toISOString(),
      extensionVersion,
      state,
    });
  } catch (e) {
    console.warn('[QG] push failed; will retry next tick', e);
    port = null;
  }
}

export function disconnect() {
  stopPing();
  try { port?.disconnect(); } catch { /* swallow */ }
  port = null;
}
```

---

## Step 4 — wire the bridge from `background.js`

After each successful `mergeSnapshot`, forward to QuotaGlass:

```diff
 import { evaluateRules } from './lib/notify.js';
 import { notify, schedule, onMessage } from './lib/browser.js';
+import { pushSnapshot } from './lib/bridge.js';

 ...

 async function mergeSnapshot(state, providerSnapshot, { source, now }) {
   if (!providerSnapshot || !providerSnapshot.provider) return state;
   const next = { ...state };
   ...
   await saveState(next);
+  // QuotaGlass desktop widget (no-op if NMH not installed).
+  pushSnapshot(next, chrome.runtime.getManifest().version);
   return next;
 }
```

`pushSnapshot` is a no-op when the native host is not installed (the
`connectNative` call fails silently and `port` stays null), so this
import is safe to land even before QuotaGlass is widely deployed.

---

## Step 5 — release notes

Bump `manifests/chrome.json` + `firefox.json` `"version"` to the next minor (e.g. `0.2.0` — first version with `"nativeMessaging"` is a permissions bump that prompts users on update). Add a CHANGELOG entry:

```
### v0.2.0 — QuotaGlass desktop bridge
- Added `"nativeMessaging"` permission so the optional QuotaGlass
  Windows widget (https://github.com/SysAdminDoc/QuotaGlass) can
  receive snapshots from the extension. No data leaves your machine.
- Added a stable `"key"` field; existing unpacked installs will get a
  new extension ID and may need to be reloaded once.
```

---

## Verification

After landing all four steps:

1. Build the extension; load unpacked in Chrome.
2. Confirm the Extension ID in `chrome://extensions/` matches the value hardcoded in `QuotaGlass/src/QuotaGlass.NMH/AllowedOrigins.cs`.
3. Install QuotaGlass; run `QuotaGlass.NMH.exe --register`.
4. Reload the extension; visit `claude.ai/settings/usage` or `chatgpt.com/codex/cloud/settings/analytics`.
5. Open `%LOCALAPPDATA%\QuotaGlass\logs\nmh-YYYY-MM-DD.log`. Expect log lines starting with `INFO` showing inbound snapshots with `claude=…` / `codex=…` bucket counts.
6. Open `%LOCALAPPDATA%\QuotaGlass\snapshot.json`. Expect a 1–10 KB JSON envelope with the documented schema.
7. Launch the widget; bucket cards should render within 1 second.
8. Open `chrome://serviceworker-internals/`, find AI-Usage_Tracker; "Running for:" should stay > 5 min instead of cycling back every 30s.
