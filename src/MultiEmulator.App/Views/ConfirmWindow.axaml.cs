using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MultiEmulator.App.Views;

public partial class ConfirmWindow : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string message) : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }
}
