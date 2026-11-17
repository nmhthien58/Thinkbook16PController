using System.Windows.Controls;
using Tb16p.Ui.ViewModels;

namespace Tb16p.Ui.Pages;

public partial class GeneralPage : Page
{
    public GeneralPage(GeneralViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
