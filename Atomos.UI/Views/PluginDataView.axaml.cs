using System;
using System.Diagnostics;
using Atomos.UI.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PluginManager.Core.Models;

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
    
    private void OnViewModClicked(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button {Tag: string url})
        {
            if (!string.IsNullOrWhiteSpace(url))
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
                    Debug.WriteLine(ex);
                }
            }
        }
    }
    
    private async void OnDownloadModClicked(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is PluginDataViewModel vm
            && sender is Button button)
        {
            if (button.DataContext is PluginMod pluginMod)
            {
                await vm.DownloadModAsync(pluginMod);
            }
        }
    }
}