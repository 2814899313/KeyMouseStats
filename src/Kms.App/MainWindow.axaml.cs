using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.App.Layout;
using Kms.App.Views;
using Kms.Core;
using Kms.Render.Theming;

namespace Kms.App;

public sealed partial class MainWindow : Window
{
    public ShellView Shell { get; }

    public MainViewModel ViewModel { get; }

    public MainWindow() : this((InputService?)null)
    {
    }

    public MainWindow(InputService? service)
        : this(new MainViewModel(service?.Loaded ?? StatsLoader.Load(), service))
    {
    }

    internal MainWindow(LoadedStats loaded, InputService? service = null, LayoutConfigService? layouts = null)
        : this(new MainViewModel(loaded, service), layouts)
    {
    }

    internal MainWindow(MainViewModel viewModel, LayoutConfigService? layouts = null)
    {
        InitializeComponent();
        Icon = AppIcons.Load();
        Title = $"键鼠统计 v{BuildInfo.Version}";
        ViewModel = viewModel;
        Shell = new ShellView(viewModel, layouts);
        ShellHost.Content = Shell;
        Opened += (_, _) => Shell.Focus();
        Closed += (_, _) => ViewModel.Dispose();
        BuildChrome();

        TitleDrag.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };
        TitleDrag.DoubleTapped += (_, _) =>
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        };
    }

    private void BuildChrome()
    {
        ChromeButtons.Children.Add(ChromeButton("▢", () =>
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }));

        Button close = ChromeButton("✕", Close);
        close.PointerEntered += (_, _) =>
        {
            close.Background = new SolidColorBrush(Color.FromArgb(200, 210, 60, 60));
            close.Foreground = Brushes.White;
        };
        close.PointerExited += (_, _) =>
        {
            close.Background = new SolidColorBrush(Color.FromArgb(
                190,
                ThemeManager.Current.Card.R,
                ThemeManager.Current.Card.G,
                ThemeManager.Current.Card.B));
            close.Foreground = new SolidColorBrush(ThemeManager.Current.Muted);
        };
        ChromeButtons.Children.Add(close);
    }

    private Button ChromeButton(string glyph, Action action)
    {
        Button button = new()
        {
            Content = glyph,
            Width = 28,
            Height = 22,
            FontSize = 10,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(190, ThemeManager.Current.Card.R, ThemeManager.Current.Card.G, ThemeManager.Current.Card.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, ThemeManager.Current.Line.R, ThemeManager.Current.Line.G, ThemeManager.Current.Line.B)),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(ThemeManager.Current.Muted)
        };
        button.Click += (_, _) => action();
        return button;
    }
}
