using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Kms.App.Apps;
using Kms.App.Layout;
using Kms.App.Theming;
using Kms.App.Widgets;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Input.Devices;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Kms.App.Pages;

/// <summary>
/// 总览：8 张紧凑 KPI、鼠标/键盘分布、区间 Top 10、洞察与 CSV 导出。
/// 4.1.0 起由 BentoGridPanel + LayoutConfigService 声明式驱动，内容区零滚动。
/// </summary>
public sealed class OverviewPage : UserControl
{
    public const string PageId = LayoutPresets.OverviewPageId;

    private readonly StatsData _data;
    private readonly InputService? _service;
    private readonly TimeScopeRange _scope;
    private readonly AppPageContext _appContext;
    private readonly OverviewWidgetModel _model;
    private readonly LayoutConfigService _layouts;
    private readonly Dictionary<string, WidgetPageModel> _models = new(StringComparer.Ordinal);
    private readonly BentoPageEditor _editor;
    private readonly TextBlock _clock = new();
    private readonly TextBlock _footer = new();
    private readonly DispatcherTimer _timer;
    private readonly Grid _root;
    private readonly WidgetLiveBus _liveBus = new();
    private bool _compact;
    private bool _tall;

    public OverviewPage(
        StatsData data,
        string title,
        string subtitle,
        Action? refresh = null,
        InputService? service = null,
        TimeScopeRange? scope = null,
        LayoutConfigService? layouts = null,
        AppPageContext? appContext = null)
    {
        _ = refresh;
        _data = data;
        _service = service;
        _scope = scope ?? TimeScopeRange.Create(TimeScope.Today, DateTime.Now);
        _appContext = appContext ?? AppPageContext.All;
        _liveBus.IsFrozen = !_appContext.IsAll;
        _model = new OverviewWidgetModel(data, _scope, service, _appContext);
        _layouts = layouts ?? LayoutConfigService.Default;
        _editor = new BentoPageEditor(PageId, _layouts, CreateContext);
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");

        _root = new Grid
        {
            Margin = new Thickness(22, 16, 22, 12),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12,
            ClipToBounds = true
        };
        _root.Children.Add(BuildHeader(title));

        Grid.SetRow(_editor, 1);
        _root.Children.Add(_editor);

        Control footer = BuildFooter();
        Grid.SetRow(footer, 2);
        _root.Children.Add(footer);

        Content = _root;
        RefreshLiveValues();

        SizeChanged += (_, _) => ApplyDensity(Bounds.Height);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshLiveValues();
        _timer.Start();
    }

    private WidgetContext CreateContext() => new()
    {
        Data = _data,
        Scope = _scope,
        AppFilter = _appContext.Filter,
        Capabilities = _appContext.Capabilities,
        Profile = _appContext.Profile,
        AppPage = _appContext,
        VariantId = "",
        Service = _service,
        Live = _liveBus,
        Model = _model,
        Models = _models
    };

    private Control BuildHeader(string title)
    {
        Grid header = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto")
        };

        TextBlock crumb = new()
        {
            Text = $"工作空间  /  {title}",
            FontSize = 9.5,
            Foreground = PageKit.Brush(PageKit.T.Muted)
        };
        header.Children.Add(crumb);

        StackPanel titleRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 30,
            FontWeight = FontWeight.Bold,
            Foreground = PageKit.Brush(PageKit.T.Text)
        });

        string state = _scope.Scope switch
        {
            TimeScope.ThisWeek => $"本周 · {_scope.RangeText}",
            TimeScope.ThisMonth => $"本月 · {_scope.RangeText}",
            _ => _model.RangeTotals.IsEmpty ? "今日 · 等待输入" : "今日 · 累积中"
        };
        titleRow.Children.Add(new Border
        {
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.14)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.30)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new Ellipse { Width = 6, Height = 6, Fill = PageKit.Brush(PageKit.T.Accent) },
                    new TextBlock
                    {
                        Text = state,
                        FontSize = 10.5,
                        Foreground = PageKit.Brush(PageKit.T.Accent)
                    }
                }
            }
        });
        Grid.SetRow(titleRow, 1);
        header.Children.Add(titleRow);

        _clock.Text = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture);
        _clock.FontSize = 11;
        _clock.Foreground = PageKit.Brush(PageKit.T.Text);
        _clock.VerticalAlignment = VerticalAlignment.Center;

        Border clockPill = new()
        {
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.86)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.75)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 8),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    new Ellipse { Width = 6, Height = 6, Fill = PageKit.Brush(PageKit.T.Green) },
                    _clock
                }
            }
        };
        Grid.SetColumn(clockPill, 1);
        Grid.SetRowSpan(clockPill, 2);
        StackPanel right = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _editor.CreateEntryButton(),
                clockPill
            }
        };
        Grid.SetColumn(right, 1);
        Grid.SetRowSpan(right, 2);
        header.Children.Add(right);
        if (ThemeArtwork.IsArtTheme(PageKit.T.Id) || ThemeArtwork.CustomActive)
        {
            return new Border
            {
                Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.76)),
                BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.45)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(PageKit.R(10, 14)),
                Padding = new Thickness(12, 8),
                Child = header
            };
        }

        return header;
    }

    private Control BuildFooter()
    {
        Grid footer = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _footer.FontSize = 10.5;
        _footer.Foreground = PageKit.Brush(PageKit.T.Muted);
        _footer.VerticalAlignment = VerticalAlignment.Center;
        _footer.TextTrimming = TextTrimming.CharacterEllipsis;
        footer.Children.Add(_footer);

        Button export = new()
        {
            Content = "⇩  导出 CSV",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(16, 7),
            CornerRadius = new CornerRadius(8),
            Background = PageKit.Gradient(PageKit.T.Accent, PageKit.T.Cyan, 0),
            Foreground = PageKit.Brush(PageKit.T.OnAccent),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        export.Click += async (_, _) => await ExportCsvAsync();
        Grid.SetColumn(export, 1);
        footer.Children.Add(export);
        return footer;
    }

    private void ApplyDensity(double height)
    {
        bool compact = PageKit.IsCompact(height);
        bool tall = PageKit.IsTall(height);
        if (_compact == compact && _tall == tall)
        {
            return;
        }

        _compact = compact;
        _tall = tall;
        _root.Margin = compact
            ? new Thickness(18, 12, 18, 8)
            : tall
                ? new Thickness(30, 24, 30, 18)
                : new Thickness(22, 16, 22, 12);
        _root.RowSpacing = compact ? 8 : tall ? 16 : 12;
        _editor.Spacing = compact ? 8 : tall ? 16 : 12;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        _liveBus.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void RefreshLiveValues()
    {
        _clock.Text = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture);
        (long keys, long clicks, long wheel, double meters, double active, int sessions) = _model.ScopeValues();
        long keyRate = _model.KeysPerMinute;
        long apm = _model.Apm;
        if (_appContext.IsAll)
        {
            _liveBus.Publish(new WidgetLiveSnapshot(
                keys,
                clicks,
                wheel,
                meters,
                active,
                sessions,
                keyRate,
                apm));
        }
        _footer.Text = _model.BuildFooterText(keyRate, _model.ClicksPerMinute);
    }

    private async Task ExportCsvAsync()
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "导出 CSV",
                SuggestedFileName = $"键鼠统计-{_scope.Label}-{_scope.End:yyyyMMdd}.csv",
                DefaultExtension = "csv",
                FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
            });
        if (file is null)
        {
            return;
        }

        StringBuilder csv = new();
        csv.AppendLine("day,keys,clicks,left,right,middle,xbuttons,wheel,move_m,active_seconds,idle_seconds,peak_apm,sessions,hold_count,hold_total_ms");
        foreach (DateTime date in _scope.DaySequence().Reverse())
        {
            string key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!_data.History.TryGetValue(key, out DayRecord? day))
            {
                continue;
            }

            csv.Append(day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(day.Keys).Append(',')
                .Append(day.Clicks).Append(',')
                .Append(day.Left).Append(',')
                .Append(day.Right).Append(',')
                .Append(day.Middle).Append(',')
                .Append(day.XButtons).Append(',')
                .Append(day.Wheel).Append(',')
                .Append(day.MoveMeters.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(day.ActiveSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(day.IdleSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(day.PeakApm).Append(',')
                .Append(day.Sessions.Count).Append(',')
                .Append(day.HoldCount).Append(',')
                .Append(day.HoldTotalMs.ToString("0.###", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        await using Stream stream = await file.OpenWriteAsync();
        await using StreamWriter writer = new(stream, new UTF8Encoding(true));
        await writer.WriteAsync(csv.ToString());
    }
}
