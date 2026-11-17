namespace Tb16p.Service;

public static class AppInfo
{
    public const string Name = "TB16P Tuner";
    public const string Tagline = "Power, fan & battery tuner for Lenovo ThinkBook 16p";
    public const string RepositoryUrl = "https://github.com/nmhthien58/Thinkbook16PController";
    public const string PayPalUrl = "https://paypal.me/nmhthien";

    /// <summary>Shared by every build of the app (old and new name) so two never run at once.</summary>
    public const string SingleInstanceMutex = @"Global\TB16PTuner.SingleInstance";

    /// <summary>Mutex of the builds from before the rename, still checked so an old copy is not run alongside.</summary>
    public const string LegacySingleInstanceMutex = @"Global\Thinkbook16PController.SingleInstance";

    /// <summary>Passed by the logon task so the app starts quietly in the tray.</summary>
    public const string MinimizedArgument = "--minimized";

    public static string Version =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";
}
