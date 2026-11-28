using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kms.App.Layout;
using Kms.App.Pages;
using Kms.App.Widgets;
using Kms.Core.Model;
using Kms.Core.Time;

namespace Kms.Golden.Tests;

[Collection("page-golden")]
public sealed class BentoPageEditorTests
{
    [Fact]
    public void CancelEdit_RestoresCommittedLayout()
    {
        GoldenHarness.EnsureAvalonia();
        using TemporaryLayouts temporary = new();
        BentoPageEditor editor = CreateEditor(temporary.Service);
        string[] before = editor.WorkingPlacements
            .Select(static placement => placement.ModuleId)
            .ToArray();

        editor.EnterEditMode();
        Assert.True(editor.IsEditMode);
        string removed = editor.WorkingPlacements[0].ModuleId;
        Assert.True(editor.TryRemoveModule(removed));
        Assert.DoesNotContain(removed, editor.WorkingPlacements.Select(static placement => placement.ModuleId));

        editor.CancelEdit();
        Assert.False(editor.IsEditMode);
        Assert.Equal(before, editor.WorkingPlacements.Select(static placement => placement.ModuleId));
    }

    [Fact]
    public void AddModule_RejectsForeignPageAndSucceedsAfterSpaceIsFreed()
    {
        GoldenHarness.EnsureAvalonia();
        using TemporaryLayouts temporary = new();
        BentoPageEditor editor = CreateEditor(temporary.Service);
        editor.EnterEditMode();

        Assert.False(editor.TryAddModule("hourly.summary_strip"));
        Assert.True(editor.TryRemoveModule("overview.insight_bar"));
        Assert.True(editor.TryAddModule("overview.insight_bar"));
        Assert.Contains(editor.WorkingPlacements, placement => placement.ModuleId == "overview.insight_bar");
    }

    [Fact]
    public void CatalogDrawer_OnlyExposesCurrentPageModules()
    {
        GoldenHarness.EnsureAvalonia();
        using TemporaryLayouts temporary = new();
        BentoPageEditor editor = CreateEditor(temporary.Service);
        editor.EnterEditMode();

        List<WidgetDescriptor> catalog = editor.FilteredModules();
        Assert.NotEmpty(catalog);
        Assert.All(catalog, descriptor =>
            Assert.True(descriptor.IsAvailableOn(PageId.Overview), descriptor.ModuleId));
        Assert.DoesNotContain(catalog, descriptor => descriptor.ModuleId == "trends.kpi.total");
        Assert.DoesNotContain(catalog, descriptor => descriptor.ModuleId == "chars.kpi.total");
        Assert.Contains(catalog, descriptor => descriptor.ModuleId == "hardware.peripheral_wear");
        Assert.False(editor.TryAddModule("trends.kpi.total"));
    }

    [Fact]
    public async Task SaveEdit_PersistsWorkingLayoutAndExits()
    {
        GoldenHarness.EnsureAvalonia();
        using TemporaryLayouts temporary = new();
        await temporary.Service.SavePageLayoutAsync(
            LayoutPresets.OverviewPageId,
            [
                new WidgetPlacement
                {
                    ModuleId = "overview.kpi.keys",
                    VariantId = "sparkline",
                    Col = 0,
                    Row = 0,
                    ColSpan = 3,
                    RowSpan = 1
                }
            ],
            CancellationToken.None);

        BentoPageEditor editor = CreateEditor(temporary.Service);
        editor.EnterEditMode();
        Assert.True(editor.TryAddModule("insights.focus_pulse"));
        await editor.SaveAsync();

        Assert.False(editor.IsEditMode);
        PageLayoutConfig saved = temporary.Service.GetPageLayout(LayoutPresets.OverviewPageId);
        Assert.Contains(saved.Placements, placement => placement.ModuleId == "overview.kpi.keys");
        Assert.Contains(saved.Placements, placement => placement.ModuleId == "insights.focus_pulse");
        Assert.Equal(saved.Placements.Count, editor.CommittedPlacements.Count);
    }

    [Fact]
    public void LockedEditor_HasNoScrollViewerAndStartsWithNoEditingChrome()
    {
        GoldenHarness.EnsureAvalonia();
        using TemporaryLayouts temporary = new();
        BentoPageEditor editor = CreateEditor(temporary.Service);
        Window window = new()
        {
            Width = 900,
            Height = 620,
            WindowDecorations = WindowDecorations.None,
            Content = editor
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(editor.IsEditMode);
            Assert.DoesNotContain(editor.GetVisualDescendants(), visual => visual is ScrollViewer);
            Assert.Contains(editor.GetVisualDescendants(), visual => visual is BentoGridPanel);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void ScenarioApply_KeepsEditorLayoutValid()
    {
        GoldenHarness.EnsureAvalonia();
        using TemporaryLayouts temporary = new();
        BentoPageEditor editor = CreateEditor(temporary.Service);
        editor.EnterEditMode();
        LayoutScenario geek = LayoutScenarios.ForPage(LayoutPresets.OverviewPageId)
            .Single(scenario => scenario.Id == "overview.geek");

        Assert.True(editor.ApplyScenario(geek));
        Assert.False(LayoutValidation.HasOverlap(editor.WorkingPlacements));
        Assert.Contains(editor.WorkingPlacements, placement =>
            placement.ModuleId == "hardware.peripheral_wear");
        editor.CancelEdit();
    }

    [Fact]
    public void GeekScenario_RendersAtViewportWithoutOuterScroll()
    {
        GoldenHarness.EnsureAvalonia();
        using TemporaryLayouts temporary = new();
        BentoPageEditor editor = CreateEditor(temporary.Service);
        Window window = new()
        {
            Width = 928,
            Height = 730,
            WindowDecorations = WindowDecorations.None,
            Content = editor
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            editor.EnterEditMode();
            LayoutScenario geek = LayoutScenarios.ForPage(LayoutPresets.OverviewPageId)
                .Single(scenario => scenario.Id == "overview.geek");
            Assert.True(editor.ApplyScenario(geek));
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(editor.GetVisualDescendants(), visual => visual is ScrollViewer);
            Assert.True(editor.Bounds.Height <= 730.5);
            if (Environment.GetEnvironmentVariable("KMS_DUMP_EDITOR") == "1")
            {
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                using MemoryStream stream = new();
                frame.Save(stream, PngBitmapEncoderOptions.Default);
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "kms-editor-geek.png"), stream.ToArray());
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void CycleSpan_AdvancesAndRevalidatesGrid()
    {
        GoldenHarness.EnsureAvalonia();
        using TemporaryLayouts temporary = new();
        BentoPageEditor editor = CreateEditor(temporary.Service);
        editor.EnterEditMode();

        Assert.True(editor.TryCycleSpan("overview.distribution_ring"));
        WidgetPlacement resized = editor.WorkingPlacements.Single(placement =>
            placement.ModuleId == "overview.distribution_ring");
        Assert.Equal(6, resized.ColSpan);
        Assert.Equal(2, resized.RowSpan);
        Assert.False(LayoutValidation.HasOverlap(editor.WorkingPlacements));
    }

    [Fact]
    public void DragTitleBar_SwapsSameSizeCards()
    {
        GoldenHarness.EnsureAvalonia();
        using TemporaryLayouts temporary = new();
        BentoPageEditor editor = CreateEditor(temporary.Service);
        Window window = new()
        {
            Width = 928,
            Height = 730,
            WindowDecorations = WindowDecorations.None,
            Content = editor
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            editor.EnterEditMode();
            Dispatcher.UIThread.RunJobs();
            if (Environment.GetEnvironmentVariable("KMS_DUMP_EDITOR") == "1")
            {
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                using MemoryStream stream = new();
                frame.Save(stream, PngBitmapEncoderOptions.Default);
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "kms-editor-edit.png"), stream.ToArray());
            }

            BuiltWidget source = editor.Built["overview.distribution_ring"];
            BentoGridPanel grid = editor.PlacementGrid;
            Point? start = source.Frame.DragHandle.TranslatePoint(new Point(8, 8), window);
            Point? origin = grid.TranslatePoint(new Point(0, 0), window);
            Assert.NotNull(start);
            Assert.NotNull(origin);

            Size cell = BentoGridMath.GetCellSize(
                grid.Bounds.Size,
                LayoutPresets.Columns,
                LayoutPresets.Rows,
                grid.Spacing);
            Point target = new(
                origin.Value.X + 6 * (cell.Width + grid.Spacing) + cell.Width / 2,
                origin.Value.Y + 2 * (cell.Height + grid.Spacing) + cell.Height / 2);

            window.MouseDown(start.Value, MouseButton.Left);
            window.MouseMove(target);
            window.MouseUp(target, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            WidgetPlacement moved = editor.WorkingPlacements.Single(placement =>
                placement.ModuleId == "overview.distribution_ring");
            WidgetPlacement swapped = editor.WorkingPlacements.Single(placement =>
                placement.ModuleId == "keys.top5_bars");
            Assert.Equal(6, moved.Col);
            Assert.Equal(2, moved.Row);
            Assert.Equal(0, swapped.Col);
            Assert.Equal(2, swapped.Row);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void CatalogDrawer_OverlaysWithoutChangingViewportHeight()
    {
        GoldenHarness.EnsureAvalonia();
        using TemporaryLayouts temporary = new();
        BentoPageEditor editor = CreateEditor(temporary.Service);
        Window window = new()
        {
            Width = 928,
            Height = 730,
            WindowDecorations = WindowDecorations.None,
            Content = editor
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            editor.EnterEditMode();
            Dispatcher.UIThread.RunJobs();
            double height = editor.Bounds.Height;
            Button add = editor.GetVisualDescendants()
                .OfType<Button>()
                .First(button => string.Equals(button.Content as string, "＋  添加模块", StringComparison.Ordinal));
            add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(editor.Bounds.Height <= height + 0.5);
            Assert.Contains(editor.GetVisualDescendants(), visual => visual is TextBox);
            Assert.Contains(editor.GetVisualDescendants(), visual => visual is ComboBox);
            Assert.True(editor.TryRemoveModule("overview.insight_bar"));
            Assert.True(editor.TryAddModule("overview.insight_bar"));
            Assert.Contains(editor.WorkingPlacements, placement =>
                placement.ModuleId == "overview.insight_bar");
            if (Environment.GetEnvironmentVariable("KMS_DUMP_EDITOR") == "1")
            {
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                using MemoryStream stream = new();
                frame.Save(stream, PngBitmapEncoderOptions.Default);
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "kms-editor-drawer.png"), stream.ToArray());
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void EditMode_OnOverviewPage_FitsViewportWithoutOuterScroll()
    {
        GoldenHarness.EnsureAvalonia();
        using TemporaryLayouts temporary = new();
        StatsData data = GoldenFixture.LoadStats();
        DateTime day = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(record => record.Date);
        OverviewPage page = new(
            data,
            "总览",
            "今天与最近 7 天的整体节奏",
            scope: TimeScopeRange.Create(TimeScope.ThisWeek, day),
            layouts: temporary.Service);
        Window window = new()
        {
            Width = 928,
            Height = 730,
            WindowDecorations = WindowDecorations.None,
            Content = page
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Button entry = page.GetVisualDescendants()
                .OfType<Button>()
                .First(button => string.Equals(button.Content as string, "自定义布局", StringComparison.Ordinal));
            entry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(page.GetVisualDescendants(), visual => visual is ScrollViewer);
            Assert.True(page.Bounds.Height <= 730.5);
            Assert.Contains(
                page.GetVisualDescendants().OfType<TextBlock>(),
                text => string.Equals(text.Text, "布局编辑", StringComparison.Ordinal));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static BentoPageEditor CreateEditor(LayoutConfigService layouts)
    {
        StatsData data = GoldenFixture.LoadStats();
        DateTime day = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(record => record.Date);
        TimeScopeRange scope = TimeScopeRange.Create(TimeScope.ThisWeek, day);
        OverviewWidgetModel model = new(data, scope, null);
        Dictionary<string, WidgetPageModel> models = new(StringComparer.Ordinal);
        return new BentoPageEditor(
            LayoutPresets.OverviewPageId,
            layouts,
            () => new WidgetContext
            {
                Data = data,
                Scope = scope,
                VariantId = "",
                Model = model,
                Models = models
            });
    }

    private sealed class TemporaryLayouts : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "kms-editor-" + Guid.NewGuid().ToString("N"));

        public LayoutConfigService Service { get; }

        public TemporaryLayouts()
        {
            Service = new LayoutConfigService(_root);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
