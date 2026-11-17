using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Tb16p.Ui.Pages;
using Tb16p.Ui.Services;
using Tb16p.Ui.ViewModels;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace Tb16p.Ui;

public partial class MainWindow : FluentWindow
{
    private readonly AppController _app;

    public MainWindow(AppController app)
    {
        _app = app;
        DataContext = app;
        InitializeComponent();

        Navigation.SetPageProviderService(new PageProvider(app));
        Loaded += (_, _) => Navigation.Navigate(typeof(GeneralPage));
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _app.OnWindowShown(); else _app.OnWindowHidden();
        };

        app.Header.PropertyChanged += OnHeaderChanged;
        ShowHeaderState();
    }

    private void OnHeaderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HeaderViewModel.IsError) or nameof(HeaderViewModel.IsPaused) or nameof(HeaderViewModel.FanMax))
            ShowHeaderState();
    }

    /// <summary>Error in red, paused in grey, max fan in orange.</summary>
    private void ShowHeaderState()
    {
        var header = _app.Header;
        StatusIcon.Symbol = header.IsError ? SymbolRegular.ErrorCircle24
            : header.IsPaused ? SymbolRegular.PauseCircle24
            : SymbolRegular.CheckmarkCircle24;
        var statusBrush = header.IsError ? "SystemFillColorCriticalBrush"
            : header.IsPaused ? "TextFillColorTertiaryBrush"
            : "SystemFillColorSuccessBrush";
        StatusIcon.SetResourceReference(ForegroundProperty, statusBrush);
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, header.IsError ? statusBrush : "TextFillColorPrimaryBrush");

        var fanBrush = header.FanMax ? "SystemFillColorCautionBrush" : "TextFillColorPrimaryBrush";
        FanIcon.SetResourceReference(ForegroundProperty, fanBrush);
        FanText.SetResourceReference(TextBlock.ForegroundProperty, fanBrush);
        FanText.FontWeight = header.FanMax ? FontWeights.SemiBold : FontWeights.Normal;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_app.ShouldHideOnClose())
        {
            e.Cancel = true;
            Hide();
            return;
        }
        if (!_app.IsExiting)
        {
            // "Close to tray" is off: closing the window exits the app (asking about unsaved edits first).
            e.Cancel = true;
            _ = _app.ExitAsync();
        }
    }

    /// <summary>One instance per page, so pages keep their state while switching.</summary>
    private sealed class PageProvider(AppController app) : INavigationViewPageProvider
    {
        private readonly Dictionary<Type, object> _pages = [];

        public object? GetPage(Type pageType)
        {
            if (!_pages.TryGetValue(pageType, out var page))
            {
                page = pageType.Name switch
                {
                    nameof(GeneralPage) => new GeneralPage(app.General),
                    nameof(ProfilesPage) => new ProfilesPage(app.Profiles),
                    nameof(SettingsPage) => new SettingsPage(app.SettingsPage),
                    nameof(SupportPage) => new SupportPage(),
                    _ => Activator.CreateInstance(pageType)!,
                };
                _pages[pageType] = page;
            }
            return page;
        }
    }
}
