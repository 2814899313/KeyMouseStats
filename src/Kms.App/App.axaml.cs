using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kms.App.Diagnostics;
using Kms.App.Reports;
using Kms.App.Widgets;

namespace Kms.App;

public sealed partial class App : Application
{
    private TrayIcon? _tray;
    private WidgetWindow? _widget;
    private FocusPillWindow? _pill;
    private GameHudWindow? _hud;
    private FocusBreakToast? _toast;
    private InputService? _service;
    private bool _exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            bool bench = desktop.Args?.Contains("--bench-ui", StringComparer.OrdinalIgnoreCase) == true;
            bool selfcheck = desktop.Args?.Contains("--selfcheck-ui", StringComparer.OrdinalIgnoreCase) == true;
            string import = FirstRunImport.Ensure();

            LoadedStats loaded = StatsLoader.Load();
            if (!bench && !selfcheck)
            {
                try
                {
                    _service = new InputService(loaded);
                }
                catch (Exception captureError)
                {
                    // 数据库损坏/被占用时退化为只读界面，不能因为采集宿主起不来就不给用户看历史。
                    _service = null;
                    try
                    {
                        File.AppendAllText(
                            Path.Combine(Path.GetTempPath(), "kms-v4-capture-error.log"),
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {captureError}{Environment.NewLine}");
                    }
                    catch
                    {
                    }
                }
            }

            desktop.Exit += (_, _) =>
            {
                _exiting = true;
                _tray?.Dispose();
                _tray = null;
                _service?.Dispose();
                _service = null;
            };

            MainWindow main = new(_service);
            desktop.MainWindow = main;
            main.Show();
            main.Closing += (_, e) =>
            {
                if (_exiting)
                {
                    return;
                }

                e.Cancel = true;
                main.Hide();
            };

            if (selfcheck)
            {
                WidgetWindow checkWidget = new(loaded);
                FocusPillWindow checkPill = new(loaded);
                GameHudWindow checkHud = new(loaded, () => 0);
                FocusBreakToast checkToast = new();
                checkWidget.Show();
                checkPill.Show();
                checkHud.Show();
                checkToast.ShowToast("自检", "检查透明/置顶/不抢焦点窗口样式。");
                UiSelfCheck.Run(main, checkWidget, checkPill, checkHud, checkToast, desktop, () => _exiting = true);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            if (bench)
            {
                UiBenchmark.Run(main, desktop, () => _exiting = true);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            _widget = new WidgetWindow(loaded);
            _widget.Show();

            _pill = new FocusPillWindow(loaded);
            _hud = new GameHudWindow(loaded, () => _service?.Apm ?? 0);
            _toast = new FocusBreakToast();

            _tray = CreateTray(desktop, main, _widget, _pill, _hud, _toast, loaded);

            if (desktop.Args?.Contains("--report", StringComparer.OrdinalIgnoreCase) == true)
            {
                new ReportWindow(loaded.Data).Show();
            }

            // 采集宿主在构造时已完成 DB/设备装载；Start 只启动钩子线程与定时器。
            _service?.Start();
            if (_service is not null && !_service.Status.StartsWith("实时采集", StringComparison.Ordinal))
            {
                _toast?.ShowToast("采集未启动", _service.Status);
            }

            if (desktop.Args?.FirstOrDefault(argument =>
                    argument.StartsWith("--smoke", StringComparison.OrdinalIgnoreCase)) is { } smoke)
            {
                string log = Path.Combine(Path.GetTempPath(), "kms-v4-smoke.log");
                int seconds = 3;
                int separator = smoke.IndexOf('=');
                if (separator > 0)
                {
                    int.TryParse(smoke[(separator + 1)..], out seconds);
                    seconds = Math.Clamp(seconds, 1, 60);
                }

                File.WriteAllText(log,
                    $"main={main.IsVisible} widget={_widget.IsVisible} tray={_tray is not null} " +
                    $"service={_service?.Status ?? "未启动（只读）"} days={loaded.Data.History.Count} " +
                    $"source={loaded.Source} root={DataPaths.V4Root} " +
                    $"v3text={DataPaths.V3Text} v3exists={File.Exists(DataPaths.V3Text)} import={import}\n");
                DispatcherTimer.RunOnce(
                    () =>
                    {
                        string today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                        loaded.Data.History.TryGetValue(today, out Kms.Core.Model.DayRecord? day);
                        File.AppendAllText(log,
                            $"today.keys={day?.Keys ?? 0} today.clicks={day?.Clicks ?? 0} " +
                            $"today.active={day?.ActiveSeconds ?? 0} today.hold={day?.HoldCount ?? 0} " +
                            $"today.peak_apm={day?.PeakApm ?? 0} all_peak={loaded.Data.Settings.GetValueOrDefault("all_time_peak_apm")} " +
                            $"today.move_m={day?.MoveMeters ?? 0:0.###} " +
                            $"today.combos={day?.ComboCounts.Count ?? 0} today.phys={day?.PhysicalKeys.Count ?? 0} " +
                            $"today.apps={day?.Apps.Count ?? 0} " +
                            $"grouped={day?.Apps.Values.Count(app => app.HasKeyGroups) ?? 0} " +
                            $"sink.keys={_service?.CountedKeys ?? 0} sink.clicks={_service?.CountedClicks ?? 0} " +
                            $"sink.move={_service?.CountedMovePixels ?? 0:0} " +
                            $"hook.events={_service?.HookEvents ?? 0} hook.keys={_service?.HookKeyEvents ?? 0} " +
                            $"raw.mouse={_service?.RawMousePackets ?? 0} dropped={_service?.DroppedEvents ?? 0}\n");
                        File.AppendAllText(log, "shutdown\n");
                        _exiting = true;
                        desktop.Shutdown();
                    },
                    TimeSpan.FromSeconds(seconds));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private TrayIcon CreateTray(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow main,
        WidgetWindow widget,
        FocusPillWindow pill,
        GameHudWindow hud,
        FocusBreakToast toast,
        LoadedStats loaded)
    {
        NativeMenuItem showMain = new("显示主面板");
        showMain.Click += (_, _) => ShowMain(main);

        NativeMenuItem toggleWidget = new("显示/隐藏悬浮窗");
        toggleWidget.Click += (_, _) =>
        {
            if (widget.IsVisible)
            {
                widget.Hide();
            }
            else
            {
                widget.Show();
            }
        };

        NativeMenuItem cycle = new("切换悬浮窗模式");
        cycle.Click += (_, _) =>
        {
            widget.Show();
            widget.CycleMode();
        };

        NativeMenuItem togglePill = new("专注胶囊");
        togglePill.Click += (_, _) =>
        {
            if (pill.IsVisible)
            {
                pill.Hide();
            }
            else
            {
                pill.Show();
            }
        };

        NativeMenuItem toggleHud = new("Game HUD");
        toggleHud.Click += (_, _) =>
        {
            if (hud.IsVisible)
            {
                hud.Hide();
            }
            else
            {
                hud.Show();
            }
        };

        NativeMenuItem editHud = new("编辑 Game HUD…");
        editHud.Click += (_, _) =>
        {
            hud.Show();
            hud.ToggleEdit();
        };

        NativeMenuItem topmost = new("主面板置顶")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = main.Topmost
        };
        topmost.Click += (_, _) => main.Topmost = topmost.IsChecked;

        NativeMenuItem toastDemo = new("演示休息提示");
        toastDemo.Click += (_, _) => toast.ShowToast();

        NativeMenuItem report = new("报表…");
        report.Click += (_, _) =>
        {
            ReportWindow window = new(loaded.Data);
            window.Show();
        };

        NativeMenuItem exit = new("退出");
        exit.Click += (_, _) =>
        {
            _exiting = true;
            desktop.Shutdown();
        };

        NativeMenu menu = new();
        menu.Items.Add(showMain);
        menu.Items.Add(toggleWidget);
        menu.Items.Add(cycle);
        menu.Items.Add(togglePill);
        menu.Items.Add(toggleHud);
        menu.Items.Add(editHud);
        menu.Items.Add(topmost);
        menu.Items.Add(toastDemo);
        menu.Items.Add(report);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);

        TrayIcon tray = new()
        {
            Icon = AppIcons.Load(),
            ToolTipText = "键鼠统计 v4",
            Menu = menu,
            IsVisible = true
        };
        tray.Clicked += (_, _) => ShowMain(main);

        // Avalonia 12 需要把 TrayIcon 挂到 Application 的 TrayIcons 集合，
        // 仅 new TrayIcon 不会被平台托盘接管。
        TrayIcon.SetIcons(this, new TrayIcons { tray });
        return tray;
    }

    private static void ShowMain(MainWindow main)
    {
        main.Show();
        main.WindowState = WindowState.Normal;
        main.Activate();
    }
}
