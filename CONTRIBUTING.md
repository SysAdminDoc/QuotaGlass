# Contributing to QuotaGlass

Thanks for considering a contribution. QuotaGlass is small (≈4k LOC across
4 projects); the bar is "match the style and the existing single-author
history". Read this once and you'll never need to re-read it.

## Quick start

```bash
# Requires: .NET 9 SDK (winget install Microsoft.DotNet.SDK.9)
dotnet build QuotaGlass.sln -c Release
dotnet test  QuotaGlass.sln -c Release
```

Iterate on the WPF widget without spinning up the full extension chain:

```bash
dotnet run --project src/QuotaGlass.Widget -- --inject-fake-snapshot
```

Wipe local state during a tricky bug repro:

```bash
QuotaGlass.NMH.exe --purge
```

## Project layout

- `src/QuotaGlass.Shared` — pure `net9.0` classlib. Wire schema, atomic
  JSON I/O, well-known paths. **No** WPF / Windows-only deps live here.
- `src/QuotaGlass.NMH` — `net9.0-windows` console exe. Stdin/stdout
  framing, registry installer, schema-versioned ack, log rotation,
  `--collect-diagnostics`.
- `src/QuotaGlass.Widget` — `net9.0-windows10.0.19041.0` WPF + WinForms
  hybrid. Hand-rolled raw WinRT toasts, dedicated STA-thread WinEvent
  topmost enforcer, native HICON ownership, MVVM via raw INPC.
- `test/QuotaGlass.Tests` — xUnit. New tests should mirror the existing
  fixture style (no fluent assertions, no Moq).

## Style

- **No `Co-Authored-By:` trailer in commits.** Existing history is
  single-author; match it.
- C# 12+ patterns: `using` declarations, file-scoped namespaces, target-typed
  `new`, primary constructors when natural. `Nullable` is on; respect it.
- No external NuGet packages without a written reason. QuotaGlass deliberately
  avoids `Microsoft.Toolkit.Uwp.Notifications`, `MaterialDesignThemes`,
  `H.NotifyIcon.Wpf`, etc. Each one we'd add costs CVE surface, install size,
  and AOT-compatibility risk. See [RESEARCH_REPORT.md](RESEARCH_REPORT.md).
- WPF/WinForms type collisions: add explicit `using Brush = …` aliases at
  the top of any file that touches both (already a known gotcha in
  `RadialRing.cs`, `App.xaml.cs`, `TrayIconService.cs`).
- All `Services/` types are window-thread-affine unless explicitly designed
  otherwise (see `TopMostEnforcer` for an STA-on-bg-thread example).

## Pull requests

- Reference a roadmap item ID (`R3-P0-*`, `R3-P1-*`, `NX-*`, `L-*`, `F-A*`,
  `F-N*`) in the PR title when applicable.
- Update the corresponding ROADMAP.md checkbox and CHANGELOG.md `[Unreleased]`
  section in the same PR.
- CI (`.github/workflows/ci.yml`) runs `dotnet build -c Release` +
  `dotnet test -c Release` + `dotnet list package --vulnerable` on every
  push. Keep it green.

## What goes where

| Change | Touch |
| --- | --- |
| New alarm rule family | `Services/AlarmScheduler.cs` + `Services/FiredRulesStore.cs` |
| New settings field | `Services/SettingsStore.cs` + `ViewModels/SettingsPanelViewModel.cs` + `Views/MainWindow.xaml` |
| New theme | new `Theme/Catppuccin*.xaml` + `Services/ThemeService.cs` Apply path |
| New tray menu entry | `Services/TrayIconService.cs` event + handler wiring in `Views/MainWindow.xaml.cs` |
| Cross-repo extension wire change | `docs/extension-integration.md` first, code second |

## Decisions captured

See [ROADMAP.md](ROADMAP.md) §"Rejected" for the long list of "we
considered this and decided against it" — pill backdrops, telemetry,
paid tier, Tauri port, Chromium cookie reads, MSIX packaging, Jira/Toggl
integrations.

## Security

See [SECURITY.md](SECURITY.md). **Do NOT** file security-relevant bugs in
the public issue tracker.
