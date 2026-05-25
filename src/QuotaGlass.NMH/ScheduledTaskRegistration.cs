using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using QuotaGlass.Shared;

namespace QuotaGlass.NMH;

/// <summary>
/// R4-N4 — register / unregister the per-user Scheduled Task that runs
/// <c>QuotaGlass.NMH.exe --poll-credentials</c> at logon + every N minutes.
///
/// We shell out to <c>schtasks.exe</c> rather than the COM Task Scheduler
/// 2.0 API because (a) `schtasks.exe` is on every supported Windows build
/// from 1809+, (b) the XML schema documented at
/// https://learn.microsoft.com/en-us/windows/win32/taskschd/task-scheduler-schema
/// is straightforward, and (c) it avoids pulling in
/// <c>Microsoft.Win32.TaskScheduler</c> which would re-introduce the
/// NuGet-package surface Pass 2 R2-P0-01 worked hard to remove.
///
/// Behavior:
/// - Only registers the task when at least one credential file is present
///   (Claude Code, Codex CLI, or Hermes). Users without the CLIs pay zero
///   cost and don't see a stray task in Task Scheduler.
/// - Task name: <c>QuotaGlass.CredentialPoll</c> in the user's root folder.
/// - Trigger: at logon + every 30 minutes after that.
/// - Action: invoke this same NMH binary with <c>--poll-credentials</c>.
/// - Uninstall: <c>--unregister</c> removes the task.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ScheduledTaskRegistration
{
    public const string TaskName = "QuotaGlass.CredentialPoll";

    public static bool AnyCredentialFilePresent()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return false;
        var paths = new[]
        {
            Path.Combine(home, ".claude", ".credentials.json"),
            Path.Combine(home, ".codex", "auth.json"),
            Path.Combine(home, ".hermes", "auth.json"),
        };
        return paths.Any(File.Exists);
    }

    public static bool TryRegister(string exePath, int intervalMinutes = 30)
    {
        if (!AnyCredentialFilePresent())
        {
            Logger.Info("No Claude Code / Codex / Hermes credential files present; skipping scheduled task registration.");
            return false;
        }

        try
        {
            var xmlPath = Path.Combine(Path.GetTempPath(), $"QuotaGlass-task-{Guid.NewGuid():N}.xml");
            File.WriteAllText(xmlPath, BuildTaskXml(exePath, intervalMinutes), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            // /F overwrites if exists. /XML reads the schedule from our file.
            // Per-user task — no /RU SYSTEM, no /RL HIGHEST.
            var psi = new ProcessStartInfo("schtasks.exe", $"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);

            try { File.Delete(xmlPath); } catch { /* best-effort */ }

            if (p is null || p.ExitCode != 0)
            {
                var stderr = p?.StandardError.ReadToEnd() ?? "(no stderr)";
                Logger.Warn($"schtasks /Create exit={p?.ExitCode}, stderr={stderr.Trim()}");
                return false;
            }

            Logger.Info($"Registered Scheduled Task {TaskName} (interval {intervalMinutes} min).");
            Console.Error.WriteLine($"  Scheduled Task {TaskName} registered ({intervalMinutes}-min interval).");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("scheduled-task registration failed", ex);
            return false;
        }
    }

    public static bool TryUnregister()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
            // ExitCode != 0 typically means the task didn't exist — fine on uninstall.
            if (p?.ExitCode == 0)
            {
                Logger.Info($"Unregistered Scheduled Task {TaskName}.");
                Console.Error.WriteLine($"  Scheduled Task {TaskName} unregistered.");
            }
            return p?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Error("scheduled-task unregistration failed", ex);
            return false;
        }
    }

    private static string BuildTaskXml(string exePath, int intervalMinutes)
    {
        // Task Scheduler 1.2 schema. Stable since Windows 7; works on 1809+.
        // We escape <Command> and <Arguments> contents via XmlEscape since
        // exePath can contain &, < if the user has an unusual install path.
        var safeExe = XmlEscape(exePath);
        var iso = $"PT{intervalMinutes}M";
        var startBoundary = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>QuotaGlass credential poll (F-N1). Polls Claude Code / Codex / Hermes credentials for quota usage.</Description>
    <URI>\{TaskName}</URI>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <Delay>PT1M</Delay>
    </LogonTrigger>
    <CalendarTrigger>
      <StartBoundary>{startBoundary}</StartBoundary>
      <Enabled>true</Enabled>
      <ScheduleByDay>
        <DaysInterval>1</DaysInterval>
      </ScheduleByDay>
      <Repetition>
        <Interval>{iso}</Interval>
        <Duration>P1D</Duration>
        <StopAtDurationEnd>false</StopAtDurationEnd>
      </Repetition>
    </CalendarTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <AllowStartIfOnBatteries>true</AllowStartIfOnBatteries>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <StartWhenAvailable>true</StartWhenAvailable>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{safeExe}</Command>
      <Arguments>--poll-credentials --interval-minutes {intervalMinutes}</Arguments>
    </Exec>
  </Actions>
</Task>";
    }

    private static string XmlEscape(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");
}
