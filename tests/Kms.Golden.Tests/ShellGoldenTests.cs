using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kms.App;
using Kms.App.Platform;
using Kms.App.Theming;
using Kms.App.Views;
using Kms.App.Widgets;
using Kms.Core.Time;
using Kms.Render.Theming;

namespace Kms.Golden.Tests;

/// <summary>完整外壳（侧栏 + 总览）的整窗基线。</summary>
[Collection("page-golden")]
public sealed class ShellGoldenTests
{
    [Fact]
    public void Shell_Overview_MatchesBaseline()
    {
        Capture("shell-overview", data => data.Settings["theme_id"] = "0");
    }

    [Fact]
    public void MainWindow_MatchesBaseline()
    {
        Capture("shell-window", data => data.Settings["theme_id"] = "0", mainWindow: true);
    }

    [Fact]
    public void Shell_CustomTheme_MatchesBaseline()
    {
        Capture("shell-custom", data =>
        {
            data.Settings["theme_id"] = "0";
            data.Settings["custom_theme_path"] = System.IO.Path.Combine(
                GoldenFixture.RepoRoot(), "assets", "ori-overview.jpg");
            data.Settings["custom_theme_name"] = "自定义皮肤";
        });
    }

    [Fact]
    public void Shell_ArtTheme_MatchesBaseline()
    {
        Capture("shell-gujian", data => data.Settings["theme_id"] = "12");
    }

    [Fact]
    public void Shell_Overview_Week_MatchesBaseline()
    {
        Capture("shell-overview-week", data => data.Settings["theme_id"] = "0", week: true);
    }

    [Fact]
    public void Shell_TimeScopePersistsAcrossPages()
    {
        GoldenHarness.EnsureAvalonia();
        Kms.Core.Model.StatsData data = GoldenFixture.LoadStats();
        DateTime fixtureDay = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(day => day.Date);
        LoadedStats stats = new(data, "v4 文本 · golden", "实时采集中");
        MainViewModel viewModel = new(stats, null, new GlobalTimeScopeService(() => fixtureDay));
        viewModel.SelectedTimeScope = TimeScope.ThisWeek;
        TimeScopeRange expected = viewModel.Range;
        ShellView shell = new(viewModel);
        Window window = new()
        {
            Width = 1180,
            Height = 760,
            WindowDecorations = WindowDecorations.None,
            Content = shell
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            shell.SelectPage(1);
            Dispatcher.UIThread.RunJobs();
            shell.SelectPage(6);
            Dispatcher.UIThread.RunJobs();
            shell.SelectPage(0);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(TimeScope.ThisWeek, viewModel.SelectedTimeScope);
            Assert.Equal(expected, viewModel.Range);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void Shell_OverviewAndApps_UseBentoGridWithoutScroll()
    {
        GoldenHarness.EnsureAvalonia();
        Kms.Core.Model.StatsData data = GoldenFixture.LoadStats();
        DateTime fixtureDay = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(day => day.Date);
        LoadedStats stats = new(data, "v4 文本 · golden", "实时采集中");
        MainViewModel viewModel = new(stats, null, new GlobalTimeScopeService(() => fixtureDay));
        ShellView shell = new(viewModel, GoldenLayouts.Empty);
        Window window = new()
        {
            Width = 1180,
            Height = 760,
            WindowDecorations = WindowDecorations.None,
            Content = shell
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(shell.GetVisualDescendants(), visual => visual is BentoGridPanel);
            Assert.Equal(11, shell.GetVisualDescendants().OfType<BentoCardFrame>().Count());
            Assert.DoesNotContain(shell.GetVisualDescendants(), visual => visual is ScrollViewer);

            shell.SelectPage(4);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(shell.GetVisualDescendants(), visual => visual is BentoGridPanel);
            Assert.Equal(4, shell.GetVisualDescendants().OfType<BentoCardFrame>().Count());
            Assert.DoesNotContain(shell.GetVisualDescendants(), visual => visual is ScrollViewer);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void Shell_AllNinePages_UseBentoGridWithoutScroll()
    {
        GoldenHarness.EnsureAvalonia();
        Kms.Core.Model.StatsData data = GoldenFixture.LoadStats();
        DateTime fixtureDay = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(day => day.Date);
        LoadedStats stats = new(data, "v4 文本 · golden", "实时采集中");
        MainViewModel viewModel = new(stats, null, new GlobalTimeScopeService(() => fixtureDay));
        ShellView shell = new(viewModel, GoldenLayouts.Empty);
        Window window = new()
        {
            Width = 1180,
            Height = 760,
            WindowDecorations = WindowDecorations.None,
            Content = shell
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            for (int page = 0; page < shell.PageCount; page++)
            {
                shell.SelectPage(page);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(shell.GetVisualDescendants(), visual => visual is BentoGridPanel);
                Assert.DoesNotContain(shell.GetVisualDescendants(), visual => visual is ScrollViewer);
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void Shell_Compact_MatchesBaseline()
    {
        GoldenHarness.EnsureAvalonia();
        Kms.Core.Model.StatsData data = GoldenFixture.LoadStats();
        data.Settings["theme_id"] = "0";
        DateTime fixtureDay = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(day => day.Date);
        LoadedStats stats = new(data, "v4 文本 · golden", "实时采集中");
        MainViewModel viewModel = new(stats, null, new GlobalTimeScopeService(() => fixtureDay));
        Window window = new()
        {
            Width = 960,
            Height = 640,
            WindowDecorations = WindowDecorations.None,
            Content = new ShellView(viewModel, GoldenLayouts.Empty)
        };
        byte[] actual = GoldenHarness.CaptureWindow(window);
        GoldenHarness.AssertPng("shell-compact", actual);
    }

    [Fact]
    public void Shell_Keys_Tall_MatchesBaseline()
    {
        GoldenHarness.EnsureAvalonia();
        Kms.Core.Model.StatsData data = GoldenFixture.LoadStats();
        data.Settings["theme_id"] = "0";
        DateTime fixtureDay = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(day => day.Date);
        LoadedStats stats = new(data, "v4 文本 · golden", "实时采集中");
        MainViewModel viewModel = new(stats, null, new GlobalTimeScopeService(() => fixtureDay))
        {
            SelectedTimeScope = TimeScope.ThisWeek
        };
        ShellView shell = new(viewModel);
        shell.SelectPage(3);
        Window window = new()
        {
            Width = 1800,
            Height = 1080,
            WindowDecorations = WindowDecorations.None,
            Content = shell
        };
        byte[] actual = GoldenHarness.CaptureWindow(window);
        GoldenHarness.AssertPng("shell-keys-tall", actual);
    }

    [Fact]
    public void Shell_Apps_Tall_MatchesBaseline()
    {
        GoldenHarness.EnsureAvalonia();
        Kms.Core.Model.StatsData data = GoldenFixture.LoadStats();
        data.Settings["theme_id"] = "0";
        DateTime fixtureDay = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(day => day.Date);
        LoadedStats stats = new(data, "v4 文本 · golden", "实时采集中");
        MainViewModel viewModel = new(stats, null, new GlobalTimeScopeService(() => fixtureDay))
        {
            SelectedTimeScope = TimeScope.ThisWeek
        };
        bool icons = AppIconLoader.Enabled;
        AppIconLoader.Enabled = false;
        try
        {
            ShellView shell = new(viewModel);
            shell.SelectPage(4);
            Window window = new()
            {
                Width = 1800,
                Height = 1080,
                WindowDecorations = WindowDecorations.None,
                Content = shell
            };
            byte[] actual = GoldenHarness.CaptureWindow(window);
            GoldenHarness.AssertPng("shell-apps-tall", actual);
        }
        finally
        {
            AppIconLoader.Enabled = icons;
        }
    }

    [Fact]
    public void Shell_TimeScopeSelector_UpdatesOnClick()
    {
        GoldenHarness.EnsureAvalonia();
        Kms.Core.Model.StatsData data = GoldenFixture.LoadStats();
        DateTime fixtureDay = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(day => day.Date);
        LoadedStats stats = new(data, "v4 文本 · golden", "实时采集中");
        MainViewModel viewModel = new(stats, null, new GlobalTimeScopeService(() => fixtureDay));
        ShellView shell = new(viewModel);
        Window window = new()
        {
            Width = 1180,
            Height = 760,
            WindowDecorations = WindowDecorations.None,
            Content = shell
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Grid scopePanel = shell.FindControl<Grid>("ScopePanel")
                ?? throw new InvalidOperationException("找不到全局时间选择器。");
            Button[] buttons = scopePanel.Children.OfType<Button>().ToArray();
            Assert.Equal(3, buttons.Length);

            buttons[2].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TimeScope.ThisMonth, viewModel.SelectedTimeScope);
            Assert.Equal(TimeScope.ThisMonth, viewModel.Range.Scope);

            buttons = (shell.FindControl<Grid>("ScopePanel") ?? scopePanel).Children.OfType<Button>().ToArray();
            buttons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TimeScope.Today, viewModel.SelectedTimeScope);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void Shell_ChangingScopeBroadcastsToActivePage()
    {
        GoldenHarness.EnsureAvalonia();
        Kms.Core.Model.StatsData data = GoldenFixture.LoadStats();
        DateTime fixtureDay = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(day => day.Date);
        LoadedStats stats = new(data, "v4 文本 · golden", "实时采集中");
        MainViewModel viewModel = new(stats, null, new GlobalTimeScopeService(() => fixtureDay));
        ShellView shell = new(viewModel);
        Window window = new()
        {
            Width = 1180,
            Height = 760,
            WindowDecorations = WindowDecorations.None,
            Content = shell
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            shell.SelectPage(7);
            Dispatcher.UIThread.RunJobs();

            viewModel.SelectedTimeScope = TimeScope.ThisWeek;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TimeScope.ThisWeek, viewModel.Range.Scope);
            Assert.Equal(TimeScope.ThisWeek, viewModel.SelectedTimeScope);

            viewModel.SelectedTimeScope = TimeScope.Today;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TimeScope.Today, viewModel.Range.Scope);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void Shell_Compact_NoVerticalScroll()
    {
        GoldenHarness.EnsureAvalonia();
        Kms.Core.Model.StatsData data = GoldenFixture.LoadStats();
        DateTime fixtureDay = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(day => day.Date);
        LoadedStats stats = new(data, "v4 文本 · golden", "实时采集中");
        MainViewModel viewModel = new(stats, null, new GlobalTimeScopeService(() => fixtureDay));
        ShellView shell = new(viewModel);
        Window window = new()
        {
            Width = 960,
            Height = 640,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Content = shell
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            foreach (Visual visual in shell.GetVisualDescendants())
            {
                Assert.IsNotType<ScrollViewer>(visual);
                if (visual is not Layoutable { IsVisible: true } layoutable)
                {
                    continue;
                }

                Point? origin = visual.TranslatePoint(new Point(0, 0), shell);
                if (origin is null)
                {
                    continue;
                }

                double bottom = origin.Value.Y + layoutable.Bounds.Height;
                Assert.True(bottom <= shell.Bounds.Height + 0.5,
                    $"侧栏/页面的 {visual.GetType().Name} 底部 {bottom:0.#} 超出 640 视口");
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void Capture(
        string name,
        Action<Kms.Core.Model.StatsData> configure,
        bool mainWindow = false,
        bool week = false)
    {
        GoldenHarness.EnsureAvalonia();
        Application app = Application.Current
            ?? throw new InvalidOperationException("Headless Avalonia 未初始化。");
        int previousTheme = ThemeManager.Current.Id;
        bool previousCustom = ThemeArtwork.CustomActive;

        try
        {
            Kms.Core.Model.StatsData data = GoldenFixture.LoadStats();
            configure(data);
            DateTime fixtureDay = data.History.Count == 0
                ? DateTime.Today
                : data.History.Values.Max(day => day.Date);
            LoadedStats stats = new(data, "v4 文本 · golden", "实时采集中");
            MainViewModel viewModel = new(
                stats,
                null,
                new GlobalTimeScopeService(() => fixtureDay));
            if (week)
            {
                viewModel.SelectedTimeScope = TimeScope.ThisWeek;
            }

            Window window = mainWindow
                ? new MainWindow(viewModel, GoldenLayouts.Empty)
                : new Window
                {
                    Width = 1180,
                    Height = 760,
                    WindowDecorations = WindowDecorations.None,
                    Content = new ShellView(viewModel, GoldenLayouts.Empty)
                };
            byte[] actual = GoldenHarness.CaptureWindow(window);
            GoldenHarness.AssertPng(name, actual);
        }
        finally
        {
            ThemeManager.Apply(app, previousTheme);
            ThemeArtwork.CustomActive = previousCustom;
        }
    }
}
