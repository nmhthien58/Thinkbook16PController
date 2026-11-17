using System.Windows;
using System.Windows.Controls;
using Tb16p.Service;
using Tb16p.Ui.Localization;
using Tb16p.Ui.Services;

namespace Tb16p.Ui.Pages;

public partial class SupportPage : Page
{
    public SupportPage()
    {
        InitializeComponent();
        ShowVersion();
        Loc.Instance.LanguageChanged += ShowVersion;
    }

    private void ShowVersion() => VersionText.Text = $"{Loc.F("About_Version", AppInfo.Version)} · {AppInfo.Tagline}";

    private void OnPayPal(object sender, RoutedEventArgs e) => AppController.OpenUrl(AppInfo.PayPalUrl);

    private void OnGitHub(object sender, RoutedEventArgs e) => AppController.OpenUrl(AppInfo.RepositoryUrl);
}
