using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Atomos.UI.Views;

public partial class PluginDataView : UserControl
{
    public PluginDataView()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnViewModClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string url && !string.IsNullOrEmpty(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                // Log error or show notification
                Console.WriteLine($"Failed to open URL: {ex.Message}");
            }
        }
    }

    private void OnDownloadModClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string downloadUrl && !string.IsNullOrEmpty(downloadUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = downloadUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                // Log error or show notification
                Console.WriteLine($"Failed to open download URL: {ex.Message}");
            }
        }
    }
}