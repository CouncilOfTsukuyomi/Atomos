using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NLog;
using PenumbraModForwarder.UI.ViewModels;

namespace PenumbraModForwarder.UI.Views;

public partial class StandaloneInstallWindow : Window
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public StandaloneInstallWindow()
    {
        InitializeComponent();
    }
    
    /// <summary>
    /// Constructor that accepts an InstallViewModel, so we can reuse the same instance
    /// used by the main application.
    /// </summary>
    /// <param name="viewModel">The existing InstallViewModel to bind to.</param>
    public StandaloneInstallWindow(InstallViewModel viewModel)
    {
        _logger.Info("Initializing StandaloneInstallWindow with an existing InstallViewModel.");
        DataContext = viewModel;
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _logger.Info("StandaloneInstallWindow opened.");
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _logger.Info("StandaloneInstallWindow closed.");
    }
}