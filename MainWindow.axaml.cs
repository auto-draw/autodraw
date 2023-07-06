using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using SkiaSharp;
using System;
using System.IO;
using Newtonsoft.Json;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Avalonia.Interactivity;

namespace Autodraw;

public partial class MainWindow : Window
{

    public MainWindow()
    {
        InitializeComponent();
        Config.init();
    }

    public static void openSettings(object sender, RoutedEventArgs e)
    {
        new Settings().Show();
    }

}