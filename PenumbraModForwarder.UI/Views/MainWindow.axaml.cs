using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommonLib.Interfaces;
using NLog;
using PenumbraModForwarder.UI.Extensions;
using PenumbraModForwarder.UI.ViewModels;

namespace PenumbraModForwarder.UI.Views
{
    public partial class MainWindow : Window
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly IConfigurationService _configuration;

        public MainWindow()
        {
            InitializeComponent();
        }
        
        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
    
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.WindowSize = new Size(e.NewSize.Width, e.NewSize.Height);
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
    
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.WindowSize = new Size(Bounds.Width, Bounds.Height);
            }
        }

        
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        public MainWindow(IConfigurationService configuration)
        {
            _configuration = configuration;
            InitializeComponent();

            var titleBar = this.FindControl<Grid>("TitleBar");
            titleBar.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    BeginMoveDrag(e);
                }
            };

            // Direct event handling for window controls
            this.Get<Button>("MinimizeButton").Click += (s, e) =>
            {
                _logger.Info("Minimize button clicked");
                if ((bool)_configuration.ReturnConfigValue(x => x.UI.MinimiseToTray))
                {
                    HiddenWindows.HideMainWindow();
                }
                else
                {
                    WindowState = WindowState.Minimized;
                }
            };

            this.Get<Button>("CloseButton").Click += (s, e) =>
            {
                _logger.Info("Close button clicked");
                Close();
            };
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);
            _logger.Info("Window closing");
        }
    }
}