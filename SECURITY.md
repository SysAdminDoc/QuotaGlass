# Security Policy

## Supported versions

QuotaGlass ships rolling pre-1.0 releases. Security fixes land in the latest
tagged version only; older tags are not back-patched.

| Version  | Supported |
| -------- | --------- |
| 0.3.x    | Yes       |
| < 0.3    | No (please upgrade) |

## Reporting a vulnerability

**Do NOT open a public GitHub issue for security-relevant bugs.**

Use [GitHub Security Advisories — Report a vulnerability](https://github.com/SysAdminDoc/QuotaGlass/security/advisories/new)
so the report is private and we can coordinate a fix.

Acceptable proof-of-concept paths include:

- A native-messaging frame that the NMH accepts despite an unlisted caller
  origin or a malformed schema.
- A code path that lets an unprivileged user read or modify another user's
  `%LOCALAPPDATA%\QuotaGlass\` content.
- A toast XML payload that escapes the hand-rolled XML escaper in
  `Services/ToastService.cs`.
- A PowerShell self-replace path in `Services/UpdateChecker.cs` that lets a
  network-positioned attacker substitute a different EXE.
- A `--collect-diagnostics` zip that leaks identifiers we promised to redact
  (`orgId`, `accountId`, full WAV paths).

## What QuotaGlass commits to

- Snapshots, settings, logs, and diagnostics stay on your machine. The only
  outbound network call is `GET api.github.com/repos/SysAdminDoc/QuotaGlass/releases/latest`
  when you click "Check for updates…".
- Native messaging origin list is the single source of truth at
  `src/QuotaGlass.NMH/AllowedOrigins.cs`. We do not accept inbound frames
  from unlisted extensions.
- JSON deserialization is depth-capped at 16 via the source-gen
  `SnapshotJsonContext`; deeper payloads are rejected with
  `"detail":"max-depth-exceeded"`.
- `--purge` wipes `%LOCALAPPDATA%\QuotaGlass\` for clean re-install.

## Response time

- Acknowledgment: within 5 business days.
- Assessment: within 14 business days.
- Fix or formal "won't fix": within 30 business days for high/critical,
  60 days for moderate/low.
