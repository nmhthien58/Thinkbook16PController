using System.Windows.Controls;
using Tb16p.Ui.ViewModels;

namespace Tb16p.Ui.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
