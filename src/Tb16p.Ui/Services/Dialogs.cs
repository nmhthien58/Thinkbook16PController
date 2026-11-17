using System.Windows;
using Tb16p.Service;
using Tb16p.Ui.Localization;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace Tb16p.Ui.Services;

public enum Choice
{
    Primary,
    Secondary,
    Cancel,
}

/// <summary>Fluent message boxes with localized buttons.</summary>
public static class Dialogs
{
    public static async Task AlertAsync(string message)
    {
        var box = Create(message);
        box.CloseButtonText = Loc.T("Btn_Ok");
        await box.ShowDialogAsync();
    }

    public static async Task<bool> ConfirmAsync(string message)
    {
        var box = Create(message);
        box.PrimaryButtonText = Loc.T("Btn_Yes");
        box.CloseButtonText = Loc.T("Btn_No");
        return await box.ShowDialogAsync() == MessageBoxResult.Primary;
    }

    /// <summary>Save / don't save / cancel.</summary>
    public static async Task<Choice> AskSaveAsync(string message)
    {
        var box = Create(message);
        box.PrimaryButtonText = Loc.T("Btn_Save");
        box.SecondaryButtonText = Loc.T("Btn_Discard");
        box.CloseButtonText = Loc.T("Btn_Cancel");
        return await box.ShowDialogAsync() switch
        {
            MessageBoxResult.Primary => Choice.Primary,
            MessageBoxResult.Secondary => Choice.Secondary,
            _ => Choice.Cancel,
        };
    }

    private static MessageBox Create(string message)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w is not MessageBox)
            ?? Application.Current?.MainWindow;
        return new MessageBox
        {
            Title = AppInfo.Name,
            Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            PrimaryButtonAppearance = ControlAppearance.Primary,
            Owner = owner is { IsVisible: true } ? owner : null,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
    }
}
