using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Autodraw;

public partial class MessageBox : Window
{
    public static void ShowMessageBox(string title, string description, string icon = "info")
    {

    }

    public MessageBox()
    {
        InitializeComponent();
    }
}