using System.Windows.Controls;
using Tb16p.Ui.ViewModels;

namespace Tb16p.Ui.Pages;

public partial class ProfilesPage : Page
{
    public ProfilesPage(ProfilesViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        // The page instance is reused; Loaded runs every time it is navigated to.
        Loaded += (_, _) => viewModel.SelectActive();
    }
}
