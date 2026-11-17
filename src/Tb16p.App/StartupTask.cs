using System.Diagnostics;
using System.Security;
using System.Security.Principal;

namespace Tb16p.App;

/// <summary>
/// "Start with Windows" for an app that needs admin rights: a Task Scheduler entry that
/// runs at logon with highest privileges, so there is no UAC prompt on every boot.
/// Created from XML because schtasks' command line keeps the defaults "only start on AC
/// power" and "stop after 3 days", both wrong for a tray app on a laptop.
/// </summary>
public static class StartupTask
{
    private const string TaskName = "Thinkbook16PController";

    public static bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static void Enable()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the app path.");
        var user = WindowsIdentity.GetCurrent().Name;
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                  <Arguments>{Program.MinimizedArgument}</Arguments>
                  <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        var xmlPath = Path.Combine(Path.GetTempPath(), $"{TaskName}.task.xml");
        File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);
        try
        {
            var code = RunSchtasks($"/Create /F /TN \"{TaskName}\" /XML \"{xmlPath}\"");
            if (code != 0)
                throw new InvalidOperationException($"schtasks /Create failed with exit code {code}.");
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    /// <summary>
    /// Rewrites an existing task so it points at the running exe, e.g. after the app moved
    /// from a dev build folder to .\app. Does nothing when the task is not enabled.
    /// </summary>
    public static void RefreshIfEnabled()
    {
        if (IsEnabled())
            Enable();
    }

    public static void Disable()
    {
        var code = RunSchtasks($"/Delete /F /TN \"{TaskName}\"");
        if (code != 0)
            throw new InvalidOperationException($"schtasks /Delete failed with exit code {code}.");
    }

    private static int RunSchtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit(10_000);
        return process.ExitCode;
    }
}
