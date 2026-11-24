using Avalonia.Controls;
using Avalonia.Interactivity;
using MultiEmulator.App.ViewModels;

namespace MultiEmulator.App.Views;

public partial class PadMappingWindow : Window
{
    public PadMappingWindow()
    {
        InitializeComponent();
    }

    public PadMappingWindow(PadMappingViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
