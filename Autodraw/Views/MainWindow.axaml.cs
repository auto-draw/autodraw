using System;
using Autodraw.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace Autodraw.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // App.axaml.cs handles the DataContext which is nice!
    }
}