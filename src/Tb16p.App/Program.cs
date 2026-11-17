using Tb16p.Service;
using Tb16p.Core.Config;

namespace Tb16p.App;

internal static class Program
{
    /// <summary>Passed by the logon task so the app starts quietly in the tray.</summary>
    public const string MinimizedArgument = AppInfo.MinimizedArgument;

    [STAThread]
    private static void Main(string[] args)
    {
        using var single = new Mutex(true, AppInfo.SingleInstanceMutex, out var first);
        if (!first)
            return;

        ApplicationConfiguration.Initialize();

        AppSettings settings;
        try
        {
            settings = AppSettings.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không đọc được {AppSettings.FilePath}:\n\n{ex.Message}", "Thinkbook16PController",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Log.Info($"Start, settings: {AppSettings.FilePath}");
        try
        {
            StartupTask.RefreshIfEnabled();
        }
        catch (Exception ex)
        {
            Log.Error($"Could not refresh the logon task: {ex.Message}");
        }
        var app = new TrayApp(settings);
        if (!args.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase))
            app.ShowSettingsWindow();
        Application.Run(app);
    }
}
