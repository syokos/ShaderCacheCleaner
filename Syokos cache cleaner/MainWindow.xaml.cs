using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ShaderCacheCleaner.Helpers;
using ShaderCacheCleaner.Services;
using ShaderCacheCleaner.ViewModels;

namespace ShaderCacheCleaner;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly IconManager _icons = new();

    public MainWindow(MainViewModel viewModel)
    {
        // Fond sombre avant tout rendu (évite le flash blanc)
        Background = DarkTitleBar.WindowBackgroundBrush;

        SourceInitialized += (_, _) => DarkTitleBar.ApplyImmediate(this);

        InitializeComponent();

        _vm = viewModel;
        DataContext = _vm;
        _icons.ApplyToWindow(this);
        DarkTitleBar.Apply(this);

        Loaded += OnLoaded;
        ContentRendered += (_, _) => DarkTitleBar.ApplyImmediate(this);
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        DarkTitleBar.ApplyImmediate(this);
        UpdateModeBorders();
        UpdateBarWidth();
        await _vm.InitializeAsync();
    }

    private void ContentScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ScrollViewer sv)
            return;

        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsCs2Selected)
            or nameof(MainViewModel.IsFullSelected)
            or nameof(MainViewModel.SelectedMode))
        {
            UpdateModeBorders();
        }

        if (e.PropertyName is nameof(MainViewModel.BarFill))
            UpdateBarWidth();
    }

    private void BarTrack_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateBarWidth();

    private void UpdateBarWidth()
    {
        if (BarTrack is null || BarFill is null)
            return;

        BarFill.Width = Math.Max(0, BarTrack.ActualWidth * Math.Clamp(_vm.BarFill, 0, 1));
    }

    private void UpdateModeBorders()
    {
        if (Cs2Button is null || FullButton is null)
            return;

        var green = new SolidColorBrush(Color.FromRgb(0x76, 0xB9, 0x00));
        var gray = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        Cs2Button.BorderBrush = _vm.IsCs2Selected ? green : gray;
        FullButton.BorderBrush = _vm.IsFullSelected ? green : gray;
    }
}
