using System;
using System.Diagnostics;
using Atomos.UI.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommonLib.Models;

namespace Atomos.UI.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}