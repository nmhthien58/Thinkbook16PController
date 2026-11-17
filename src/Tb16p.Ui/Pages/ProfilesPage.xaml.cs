using System.Windows.Controls;
using Tb16p.Ui.ViewModels;

namespace Tb16p.Ui.Pages;

public partial class ProfilesPage : Page
{
    public ProfilesPage(ProfilesViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
