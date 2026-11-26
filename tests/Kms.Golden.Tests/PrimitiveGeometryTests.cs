using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.Golden.Tests;

/// <summary>12 个图表原语的纯几何测试；不创建窗口、不读取 DPI、不做真实截图。</summary>
public sealed class PrimitiveGeometryTests
{
    private static ThemeTokens Theme => ThemeCatalog.Get(0);

    private static LineSeries Line(params double[] values) =>
        new("series", values, Colors.Cyan);

    private static BarDatum Bar(double value) => new(value);

    // ---------- RoundRect ----------

    [Fact]
    public void RoundRect_ClampsRadiusToHalfShortSide()
    {
        double radius = RoundRect.ClampRadius(new Rect(0, 0, 100, 40), 50);

        Assert.Equal(20, radius, 6);
    }

    [Fact]
    public void RoundRect_NegativeRadiusBecomesZero()
    {
        Assert.Equal(0, RoundRect.ClampRadius(new Rect(0, 0, 20, 20), -5));
    }

    [Fact]
    public void RoundRect_UsesThemeRadius()
    {
        Assert.Equal(13, RoundRect.RadiusFor(new Rect(0, 0, 100, 100), Theme), 6);
    }

    [Fact]
    public void RoundRect_ZeroThemeRadiusStaysSquare()
    {
        ThemeTokens square = ThemeCatalog.Get(4);

        Assert.Equal(0, square.Radius);
        Assert.Equal(0, RoundRect.RadiusFor(new Rect(0, 0, 100, 100), square));
    }

    [Fact]
    public void RoundRect_ShapeHasUniformRadii()
    {
        RoundedRect shape = RoundRect.Shape(new Rect(10, 20, 100, 60), Theme);

        Assert.Equal(13, shape.RadiiTopLeft.X, 6);
        Assert.Equal(13, shape.RadiiTopLeft.Y, 6);
        Assert.Equal(shape.RadiiTopLeft, shape.RadiiTopRight);
        Assert.Equal(shape.RadiiTopLeft, shape.RadiiBottomLeft);
    }

    // ---------- Card ----------

    [Fact]
    public void Card_ContentIsInsetByPadding()
    {
        CardGeometry card = Card.Layout(new Rect(0, 0, 100, 80), Theme, padding: 10);

        Assert.Equal(new Rect(10, 10, 80, 60), card.Content);
    }

    [Fact]
    public void Card_RadiusIsClampedByHeight()
    {
        CardGeometry card = Card.Layout(new Rect(0, 0, 100, 20), Theme, padding: 0);

        Assert.Equal(10, card.Radius, 6);
    }

    [Fact]
    public void Card_SplitRowsKeepsGap()
    {
        IReadOnlyList<Rect> rows = Card.SplitRows(new Rect(0, 0, 100, 100), 2, gap: 10);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new Rect(0, 0, 100, 45), rows[0]);
        Assert.Equal(new Rect(0, 55, 100, 45), rows[1]);
    }

    [Fact]
    public void Card_SplitColumnsKeepsGap()
    {
        IReadOnlyList<Rect> columns = Card.SplitColumns(new Rect(0, 0, 100, 50), 3, gap: 5);

        Assert.Equal(3, columns.Count);
        Assert.Equal(30, columns[0].Width, 6);
        Assert.Equal(0, columns[0].X, 6);
        Assert.Equal(35, columns[1].X, 6);
        Assert.Equal(70, columns[2].X, 6);
    }

    [Fact]
    public void Card_SplitZeroCountIsEmpty()
    {
        Assert.Empty(Card.SplitRows(new Rect(0, 0, 10, 10), 0));
        Assert.Empty(Card.SplitColumns(new Rect(0, 0, 10, 10), -1));
    }

    // ---------- HitLayer ----------

    [Fact]
    public void HitLayer_UsesZIndexNotRegistrationOrder()
    {
        HitLayer layer = new();
        layer.Add("high", new Rect(0, 0, 50, 50), zIndex: 10);
        layer.Add("low", new Rect(0, 0, 50, 50), zIndex: 0);

        Assert.Equal("high", layer.HitTest(new Point(10, 10))?.Id);
    }

    [Fact]
    public void HitLayer_SameZIndexLastRegisteredWins()
    {
        HitLayer layer = new();
        layer.Add("first", new Rect(0, 0, 50, 50));
        layer.Add("second", new Rect(0, 0, 50, 50));

        Assert.Equal("second", layer.HitTest(new Point(10, 10))?.Id);
    }

    [Fact]
    public void HitLayer_IgnoresDisabledAndEmptyTargets()
    {
        HitLayer layer = new();
        layer.Add(new HitTarget("disabled", new Rect(0, 0, 50, 50), ZIndex: 99, Enabled: false));
        layer.Add(new HitTarget("empty", new Rect(0, 0, 0, 0), ZIndex: 98));
        layer.Add("valid", new Rect(0, 0, 50, 50));

        Assert.Equal("valid", layer.HitTest(new Point(10, 10))?.Id);
    }

    [Fact]
    public void HitLayer_HitTestAllReturnsTopDown()
    {
        HitLayer layer = new();
        layer.Add("low", new Rect(0, 0, 50, 50), zIndex: 0);
        layer.Add("high", new Rect(0, 0, 50, 50), zIndex: 10);
        layer.Add("mid", new Rect(0, 0, 50, 50), zIndex: 5);

        IReadOnlyList<HitTarget> hits = layer.HitTestAll(new Point(10, 10));

        Assert.Equal(["high", "mid", "low"], hits.Select(h => h.Id));
    }

    [Fact]
    public void HitLayer_RemoveAndClear()
    {
        HitLayer layer = new();
        layer.Add("a", new Rect(0, 0, 10, 10));
        layer.Add("b", new Rect(0, 0, 10, 10));

        Assert.True(layer.Remove("a"));
        Assert.False(layer.Remove("missing"));
        Assert.Single(layer.Targets);

        layer.Clear();
        Assert.Equal(0, layer.Count);
    }

    [Fact]
    public void HitLayer_PayloadRoundTrips()
    {
        HitLayer layer = new();
        layer.Add("chip", new Rect(0, 0, 10, 10), payload: 42, zIndex: 1);

        HitTarget hit = Assert.IsType<HitTarget>(layer.HitTest(new Point(5, 5)));
        Assert.Equal(42, hit.Payload);
    }

    [Fact]
    public void HitLayer_MissReturnsNull()
    {
        HitLayer layer = new();
        layer.Add("chip", new Rect(0, 0, 10, 10));

        Assert.Null(layer.HitTest(new Point(50, 50)));
        Assert.Empty(layer.HitTestAll(new Point(50, 50)));
    }

    // ---------- LineChart ----------

    [Fact]
    public void LineChart_EmptyDataIsSafe()
    {
        LineChartGeometry geometry = LineChart.Measure(new Rect(0, 0, 100, 50), []);

        Assert.True(geometry.IsEmpty);
        Assert.Empty(geometry.Series);
        Assert.Equal(4, geometry.YTicks.Count);
    }

    [Fact]
    public void LineChart_SinglePointIsCentered()
    {
        LineChartGeometry geometry = LineChart.Measure(
            new Rect(0, 0, 100, 50),
            [Line(5)]);

        Point point = Assert.Single(Assert.Single(geometry.Series).Points);
        Assert.Equal(50, point.X, 6);
        Assert.Empty(geometry.Series[0].Segments);
        Assert.False(geometry.IsEmpty);
    }

    [Fact]
    public void LineChart_TwoPointsSpanPlotWidth()
    {
        LineChartGeometry geometry = LineChart.Measure(
            new Rect(0, 0, 100, 50),
            [Line(0, 10)],
            new LineChartOptions(Padding: 0));

        IReadOnlyList<Point> points = geometry.Series[0].Points;
        Assert.Equal(2, points.Count);
        Assert.Equal(0, points[0].X, 6);
        Assert.Equal(100, points[1].X, 6);
        Assert.Equal(50, points[0].Y, 6);
        Assert.Equal(0, points[1].Y, 6);
    }

    [Fact]
    public void LineChart_NegativeValuesIncludeZeroBaseline()
    {
        LineChartGeometry geometry = LineChart.Measure(
            new Rect(0, 0, 100, 50),
            [Line(-10, 0)],
            new LineChartOptions(Padding: 0));

        Assert.Equal(-10, geometry.YMin, 6);
        Assert.Equal(0, geometry.YMax, 6);
        Assert.Equal(0, geometry.BaselineY, 6);
        Assert.Equal(50, geometry.Series[0].Points[0].Y, 6);
    }

    [Fact]
    public void LineChart_AllZeroValuesUseSymmetricDomain()
    {
        LineChartGeometry geometry = LineChart.Measure(
            new Rect(0, 0, 100, 50),
            [Line(0, 0, 0)]);

        Assert.Equal(-1, geometry.YMin, 6);
        Assert.Equal(1, geometry.YMax, 6);
        Assert.Equal(25, geometry.BaselineY, 6);
    }

    [Fact]
    public void LineChart_PendingTailOnlyDashesLastSegment()
    {
        LineSeries series = new("today", [1, 2, 3], Colors.Cyan, PendingTail: true);
        LineChartGeometry geometry = LineChart.Measure(new Rect(0, 0, 100, 50), [series]);

        IReadOnlyList<LineChartSegment> segments = geometry.Series[0].Segments;
        Assert.Equal(2, segments.Count);
        Assert.False(segments[0].Dashed);
        Assert.True(segments[1].Dashed);
    }

    [Fact]
    public void LineChart_ConstantValuesDoNotDivideByZero()
    {
        LineChartGeometry geometry = LineChart.Measure(
            new Rect(0, 0, 100, 50),
            [Line(5, 5)]);

        Assert.True(geometry.YMax > geometry.YMin);
        Assert.Equal(geometry.YMax, geometry.YTicks[^1], 6);
    }

    [Fact]
    public void LineChart_TicksAreMonotonic()
    {
        LineChartGeometry geometry = LineChart.Measure(
            new Rect(0, 0, 100, 50),
            [Line(0, 100)],
            new LineChartOptions(YTickCount: 5));

        Assert.Equal(5, geometry.YTicks.Count);
        Assert.Equal(0, geometry.YTicks[0], 6);
        Assert.Equal(100, geometry.YTicks[^1], 6);
        for (int i = 1; i < geometry.YTicks.Count; i++)
        {
            Assert.True(geometry.YTicks[i] > geometry.YTicks[i - 1]);
        }
    }

    [Fact]
    public void LineChart_RegisterHitsUsesPointPayload()
    {
        LineChartGeometry geometry = LineChart.Measure(
            new Rect(0, 0, 100, 50),
            [Line(0, 10)]);
        HitLayer layer = new();

        LineChart.RegisterHits(layer, geometry);

        Assert.Equal(2, layer.Count);
        HitTarget hit = Assert.IsType<HitTarget>(layer.HitTest(geometry.Series[0].Points[1]));
        LinePointHit payload = Assert.IsType<LinePointHit>(hit.Payload);
        Assert.Equal(1, payload.PointIndex);
        Assert.Equal(10, payload.Value, 6);
    }

    // ---------- BarChart ----------

    [Fact]
    public void BarChart_PositiveBarsGrowUpFromBaseline()
    {
        BarChartGeometry geometry = BarChart.Measure(
            new Rect(0, 0, 100, 50),
            [Bar(10), Bar(20)],
            new BarChartOptions(Padding: 0, GapRatio: 0.2));

        Assert.Equal(50, geometry.BaselineY, 6);
        Assert.Equal(25, geometry.Bars[0].Bounds.Height, 6);
        Assert.Equal(50, geometry.Bars[1].Bounds.Height, 6);
        Assert.Equal(0, geometry.Bars[1].Bounds.Top, 6);
        Assert.Equal(40, geometry.Bars[0].Bounds.Width, 6);
        Assert.Equal(5, geometry.Bars[0].Bounds.X, 6);
    }

    [Fact]
    public void BarChart_NegativeBarsHangFromTopBaseline()
    {
        BarChartGeometry geometry = BarChart.Measure(
            new Rect(0, 0, 100, 50),
            [Bar(-10), Bar(-20)],
            new BarChartOptions(Padding: 0));

        Assert.Equal(-20, geometry.YMin, 6);
        Assert.Equal(0, geometry.YMax, 6);
        Assert.Equal(0, geometry.BaselineY, 6);
        Assert.True(geometry.Bars[0].IsNegative);
        Assert.Equal(25, geometry.Bars[0].Bounds.Height, 6);
        Assert.Equal(0, geometry.Bars[0].Bounds.Top, 6);
    }

    [Fact]
    public void BarChart_MixedValuesPutBaselineInsidePlot()
    {
        BarChartGeometry geometry = BarChart.Measure(
            new Rect(0, 0, 100, 50),
            [Bar(-5), Bar(5)],
            new BarChartOptions(Padding: 0));

        Assert.Equal(25, geometry.BaselineY, 6);
        Assert.True(geometry.Bars[0].IsNegative);
        Assert.Equal(25, geometry.Bars[0].Bounds.Top, 6);
        Assert.Equal(25, geometry.Bars[1].Bounds.Height, 6);
    }

    [Fact]
    public void BarChart_ZeroValueKeepsSlotButZeroHeight()
    {
        BarChartGeometry geometry = BarChart.Measure(
            new Rect(0, 0, 100, 50),
            [Bar(0)],
            new BarChartOptions(Padding: 0));

        Assert.Equal(0, geometry.Bars[0].Bounds.Height);
        Assert.Equal(100, geometry.Bars[0].Slot.Width, 6);
        Assert.Equal(geometry.BaselineY, geometry.Bars[0].Bounds.Top, 6);
    }

    [Fact]
    public void BarChart_EmptyDataIsSafe()
    {
        BarChartGeometry geometry = BarChart.Measure(new Rect(0, 0, 100, 50), []);

        Assert.True(geometry.IsEmpty);
        Assert.Empty(geometry.Bars);
    }

    [Fact]
    public void BarChart_GapRatioChangesBarWidth()
    {
        BarChartGeometry geometry = BarChart.Measure(
            new Rect(0, 0, 100, 50),
            [Bar(1)],
            new BarChartOptions(Padding: 0, GapRatio: 0.5));

        Assert.Equal(50, geometry.Bars[0].Bounds.Width, 6);
    }

    [Fact]
    public void BarChart_RegisterHitsUsesSlotPayload()
    {
        BarChartGeometry geometry = BarChart.Measure(
            new Rect(0, 0, 100, 50),
            [Bar(10), Bar(20)]);
        HitLayer layer = new();

        BarChart.RegisterHits(layer, geometry);

        HitTarget hit = Assert.IsType<HitTarget>(layer.HitTest(new Point(25, 10)));
        BarHit payload = Assert.IsType<BarHit>(hit.Payload);
        Assert.Equal(0, payload.Index);
        Assert.Equal(10, payload.Value, 6);
    }

    // ---------- HeatGrid ----------

    [Fact]
    public void HeatGrid_ColorsComeFromHeatScaleFor()
    {
        long[,] counts = { { 0, 1 }, { 10, 100 } };
        HeatGridGeometry geometry = HeatGrid.Measure(new Rect(0, 0, 100, 100), counts, Theme);

        Assert.Equal(4, geometry.Cells.Count);
        Assert.Equal(100, geometry.Max);
        Assert.Equal(HeatScale.For(Theme, HeatScale.All, 100, 100), geometry.Cells[3].Fill);
        Assert.Equal(HeatScale.For(Theme, HeatScale.All, 1, 100), geometry.Cells[1].Fill);
    }

    [Fact]
    public void HeatGrid_ZeroCellUsesZeroColor()
    {
        long[,] counts = { { 0, 5 } };
        HeatGridGeometry geometry = HeatGrid.Measure(new Rect(0, 0, 100, 20), counts, Theme);

        Assert.Equal(HeatScale.Zero(Theme), geometry.Cells[0].Fill);
    }

    [Fact]
    public void HeatGrid_GapAffectsCellOffsets()
    {
        long[,] counts = { { 1, 2 }, { 3, 4 } };
        HeatGridGeometry geometry = HeatGrid.Measure(new Rect(0, 0, 100, 100), counts, Theme, gap: 2);

        Assert.Equal(49, geometry.CellWidth, 6);
        Assert.Equal(51, geometry.Cells[1].Bounds.X, 6);
        Assert.Equal(51, geometry.Cells[2].Bounds.Y, 6);
    }

    [Fact]
    public void HeatGrid_AllZeroDoesNotDivideByZero()
    {
        long[,] counts = { { 0, 0 }, { 0, 0 } };
        HeatGridGeometry geometry = HeatGrid.Measure(new Rect(0, 0, 100, 100), counts, Theme);

        Assert.Equal(0, geometry.Max);
        Assert.All(geometry.Cells, cell => Assert.Equal(HeatScale.Zero(Theme), cell.Fill));
    }

    [Fact]
    public void HeatGrid_RegisterHitsUsesCellPayload()
    {
        long[,] counts = { { 1, 2 }, { 3, 4 } };
        HeatGridGeometry geometry = HeatGrid.Measure(new Rect(0, 0, 100, 100), counts, Theme);
        HitLayer layer = new();

        HeatGrid.RegisterHits(layer, geometry);

        HitTarget hit = Assert.IsType<HitTarget>(layer.HitTest(geometry.Cells[3].Bounds.Center));
        HeatCellHit payload = Assert.IsType<HeatCellHit>(hit.Payload);
        Assert.Equal(1, payload.Row);
        Assert.Equal(1, payload.Column);
        Assert.Equal(4, payload.Count);
    }

    [Fact]
    public void HeatGrid_ChannelIsForwardedToHeatScale()
    {
        long[,] counts = { { 10 } };
        HeatGridGeometry geometry = HeatGrid.Measure(
            new Rect(0, 0, 100, 100),
            counts,
            Theme,
            channel: HeatScale.Left);

        Assert.Equal(HeatScale.ChannelAt(Theme, HeatScale.Left, 1), geometry.Cells[0].Fill);
    }

    // ---------- Sparkline ----------

    [Fact]
    public void Sparkline_EmptyDataIsSafe()
    {
        SparklineGeometry geometry = Sparkline.Measure(new Rect(0, 0, 100, 30), []);

        Assert.Empty(geometry.Points);
        Assert.True(geometry.IsFlat);
    }

    [Fact]
    public void Sparkline_SinglePointIsCentered()
    {
        SparklineGeometry geometry = Sparkline.Measure(new Rect(0, 0, 100, 30), [5]);

        Point point = Assert.Single(geometry.Points);
        Assert.Equal(50, point.X, 6);
        Assert.Equal(15, point.Y, 6);
    }

    [Fact]
    public void Sparkline_FlatValuesDrawMidline()
    {
        SparklineGeometry geometry = Sparkline.Measure(new Rect(0, 0, 100, 30), [5, 5, 5]);

        Assert.True(geometry.IsFlat);
        Assert.All(geometry.Points, point => Assert.Equal(15, point.Y, 6));
    }

    [Fact]
    public void Sparkline_ValuesMapToPlotEnds()
    {
        SparklineGeometry geometry = Sparkline.Measure(new Rect(0, 0, 100, 30), [0, 10], padding: 0);

        Assert.Equal(0, geometry.Points[0].X, 6);
        Assert.Equal(100, geometry.Points[1].X, 6);
        Assert.Equal(30, geometry.Points[0].Y, 6);
        Assert.Equal(0, geometry.Points[1].Y, 6);
    }

    [Fact]
    public void Sparkline_NegativeValuesMapLow()
    {
        SparklineGeometry geometry = Sparkline.Measure(new Rect(0, 0, 100, 30), [-10, 0], padding: 0);

        Assert.Equal(30, geometry.Points[0].Y, 6);
        Assert.Equal(0, geometry.Points[1].Y, 6);
    }

    [Fact]
    public void Sparkline_PaddingInsetsPlot()
    {
        SparklineGeometry geometry = Sparkline.Measure(new Rect(0, 0, 100, 30), [0, 10], padding: 5);

        Assert.Equal(5, geometry.Points[0].X, 6);
        Assert.Equal(95, geometry.Points[1].X, 6);
    }

    // ---------- Donut ----------

    [Fact]
    public void Donut_EqualSlicesSumToSweep()
    {
        DonutGeometry geometry = Donut.Measure(
            new Rect(0, 0, 100, 100),
            [new DonutSlice(1, Colors.Red), new DonutSlice(1, Colors.Green), new DonutSlice(1, Colors.Blue), new DonutSlice(1, Colors.Yellow)]);

        Assert.Equal(4, geometry.Total, 6);
        Assert.Equal(4, geometry.Arcs.Count);
        Assert.Equal(-90, geometry.Arcs[0].StartAngle, 6);
        Assert.Equal(0, geometry.Arcs[1].StartAngle, 6);
        Assert.Equal(180, geometry.Arcs[3].StartAngle, 6);
        Assert.Equal(360, geometry.Arcs.Sum(a => a.SweepAngle), 6);
    }

    [Fact]
    public void Donut_StartAndSweepAnglesAreHonored()
    {
        DonutGeometry geometry = Donut.Measure(
            new Rect(0, 0, 100, 100),
            [new DonutSlice(1, Colors.Cyan)],
            startAngle: 30,
            sweepAngle: 180);

        Assert.Equal(30, geometry.Arcs[0].StartAngle, 6);
        Assert.Equal(180, geometry.Arcs[0].SweepAngle, 6);
    }

    [Fact]
    public void Donut_ZeroTotalHasNoArcs()
    {
        DonutGeometry geometry = Donut.Measure(
            new Rect(0, 0, 100, 100),
            [new DonutSlice(0, Colors.Red), new DonutSlice(-1, Colors.Blue)]);

        Assert.True(geometry.IsEmpty);
        Assert.Empty(geometry.Arcs);
    }

    [Fact]
    public void Donut_NegativeAndZeroSlicesAreIgnored()
    {
        DonutGeometry geometry = Donut.Measure(
            new Rect(0, 0, 100, 100),
            [new DonutSlice(-5, Colors.Red), new DonutSlice(0, Colors.Green), new DonutSlice(10, Colors.Blue)]);

        DonutArc arc = Assert.Single(geometry.Arcs);
        Assert.Equal(10, arc.Value, 6);
        Assert.Equal(360, arc.SweepAngle, 6);
    }

    [Fact]
    public void Donut_CenterAndRadiiComeFromBounds()
    {
        DonutGeometry geometry = Donut.Measure(new Rect(10, 20, 100, 100), [new DonutSlice(1, Colors.Red)], thicknessRatio: 0.4);

        Assert.Equal(60, geometry.Center.X, 6);
        Assert.Equal(70, geometry.Center.Y, 6);
        Assert.Equal(50, geometry.OuterRadius, 6);
        Assert.Equal(30, geometry.InnerRadius, 6);
    }

    [Fact]
    public void Donut_PointAtAngleUsesScreenClockwise()
    {
        DonutGeometry geometry = Donut.Measure(new Rect(0, 0, 100, 100), [new DonutSlice(1, Colors.Red)]);

        Point top = geometry.PointAt(-90, 50);
        Point right = geometry.PointAt(0, 50);
        Point bottom = geometry.PointAt(90, 50);

        Assert.Equal(50, top.X, 6);
        Assert.Equal(0, top.Y, 6);
        Assert.Equal(100, right.X, 6);
        Assert.Equal(50, right.Y, 6);
        Assert.Equal(50, bottom.X, 6);
        Assert.Equal(100, bottom.Y, 6);
    }

    [Fact]
    public void Donut_HitTestRespectsSlicesAndHole()
    {
        DonutGeometry geometry = Donut.Measure(
            new Rect(0, 0, 100, 100),
            [new DonutSlice(1, Colors.Red), new DonutSlice(1, Colors.Blue)]);

        Assert.Equal(0, geometry.HitTest(new Point(90, 50))?.Index);
        Assert.Equal(1, geometry.HitTest(new Point(10, 50))?.Index);
        Assert.Null(geometry.HitTest(new Point(50, 50)));
        Assert.Null(geometry.HitTest(new Point(150, 50)));
    }

    [Fact]
    public void Donut_HitTestHandlesNegativeSweep()
    {
        DonutGeometry geometry = Donut.Measure(
            new Rect(0, 0, 100, 100),
            [new DonutSlice(1, Colors.Red)],
            startAngle: 0,
            sweepAngle: -180);

        Assert.Equal(0, geometry.HitTest(new Point(50, 10))?.Index);
        Assert.Null(geometry.HitTest(new Point(50, 90)));
    }

    // ---------- Legend ----------

    [Fact]
    public void Legend_SingleRowUsesEqualCells()
    {
        LegendGeometry geometry = Legend.Measure(
            new Rect(0, 0, 200, 20),
            [new LegendItem("左键", Colors.Cyan), new LegendItem("右键", Colors.Orange)]);

        Assert.Equal(2, geometry.Columns);
        Assert.Equal(1, geometry.Rows);
        Assert.Equal(94, geometry.Entries[0].Bounds.Width, 6);
        Assert.Equal(106, geometry.Entries[1].Bounds.X, 6);
    }

    [Fact]
    public void Legend_ColumnsCreateRows()
    {
        LegendGeometry geometry = Legend.Measure(
            new Rect(0, 0, 200, 60),
            [new LegendItem("a", Colors.Red), new LegendItem("b", Colors.Green), new LegendItem("c", Colors.Blue)],
            columns: 2);

        Assert.Equal(2, geometry.Columns);
        Assert.Equal(2, geometry.Rows);
        Assert.Equal(0, geometry.Entries[0].Bounds.Y, 6);
        Assert.Equal(20, geometry.Entries[2].Bounds.Y, 6);
    }

    [Fact]
    public void Legend_SwatchIsVerticallyCentered()
    {
        LegendGeometry geometry = Legend.Measure(
            new Rect(0, 0, 200, 20),
            [new LegendItem("a", Colors.Red)],
            rowHeight: 16,
            swatchSize: 8);

        Assert.Equal(4, geometry.Entries[0].Swatch.Y, 6);
        Assert.Equal(14, geometry.Entries[0].TextBounds.X, 6);
    }

    [Fact]
    public void Legend_EmptyIsSafe()
    {
        LegendGeometry geometry = Legend.Measure(new Rect(0, 0, 200, 20), []);

        Assert.Empty(geometry.Entries);
        Assert.Equal(0, geometry.Columns);
        Assert.Equal(0, geometry.Rows);
    }

    [Fact]
    public void Legend_RegisterHitsUsesLabelPayload()
    {
        LegendGeometry geometry = Legend.Measure(
            new Rect(0, 0, 200, 20),
            [new LegendItem("左键", Colors.Cyan), new LegendItem("右键", Colors.Orange)]);
        HitLayer layer = new();

        Legend.RegisterHits(layer, geometry);

        HitTarget hit = Assert.IsType<HitTarget>(layer.HitTest(geometry.Entries[1].Bounds.Center));
        LegendHit payload = Assert.IsType<LegendHit>(hit.Payload);
        Assert.Equal("右键", payload.Label);
    }

    [Fact]
    public void Legend_ChannelItemUsesChannelAccent()
    {
        LegendItem item = Legend.ChannelItem(Theme, HeatScale.Left, "左键");

        Assert.Equal(HeatScale.ChannelAccent(Theme, HeatScale.Left), item.Color);
    }

    // ---------- KeyCapGrid ----------

    [Theory]
    [InlineData(0, 87)]
    [InlineData(1, 104)]
    [InlineData(2, 87)]
    [InlineData(3, 80)]
    [InlineData(4, 67)]
    [InlineData(5, 61)]
    public void KeyCapGrid_HasSixLayoutTables(int layout, int expectedKeys)
    {
        Assert.Equal(expectedKeys, KeyCapGrid.GetLayout(layout).Count);
    }

    [Fact]
    public void KeyCapGrid_NamesAndNormalization()
    {
        Assert.Equal(6, KeyCapGrid.Names.Count);
        Assert.Equal(0, KeyCapGrid.NormalizeLayout(99));
        Assert.Equal(5, KeyCapGrid.NormalizeLayout(5));
    }

    [Fact]
    public void KeyCapGrid_MeasureKeepsKeysInsideBounds()
    {
        KeyCapGridGeometry geometry = KeyCapGrid.Measure(new Rect(0, 0, 1000, 400), KeyCapGrid.LayoutAnsi104);

        Assert.Equal(23, geometry.WidthUnits, 6);
        Assert.Equal(104, geometry.Keys.Count);
        Assert.All(geometry.Keys, key =>
        {
            Assert.True(key.Bounds.Left >= -0.001);
            Assert.True(key.Bounds.Top >= -0.001);
            Assert.True(key.Bounds.Right <= 1000.001);
            Assert.True(key.Bounds.Bottom <= 400.001);
        });
    }

    [Fact]
    public void KeyCapGrid_AnsiHasNumpadAndTklDoesNot()
    {
        Assert.Contains(KeyCapGrid.GetLayout(KeyCapGrid.LayoutAnsi104), key => key.Label == "Num");
        Assert.DoesNotContain(KeyCapGrid.GetLayout(KeyCapGrid.LayoutTkl87), key => key.Label == "Num");
    }

    [Fact]
    public void KeyCapGrid_CompactLayoutsDropFunctionRow()
    {
        Assert.Contains(KeyCapGrid.GetLayout(KeyCapGrid.Layout75), key => key.Label == "F1");
        Assert.DoesNotContain(KeyCapGrid.GetLayout(KeyCapGrid.Layout65), key => key.Label == "F1");
        Assert.Equal("` / Esc", KeyCapGrid.GetLayout(KeyCapGrid.Layout60)[0].Label);
    }

    [Fact]
    public void KeyCapGrid_HitTestFindsKeyCenter()
    {
        KeyCapGridGeometry geometry = KeyCapGrid.Measure(new Rect(0, 0, 1000, 400), KeyCapGrid.LayoutTkl87);
        KeyCap target = KeyCapGrid.GetLayout(KeyCapGrid.LayoutTkl87).First(key => key.Label == "Q");
        Rect bounds = geometry.Keys.First(key => key.Key.Label == "Q").Bounds;

        KeyCapGeometry? hit = geometry.HitTest(bounds.Center);
        Assert.NotNull(hit);
        Assert.Equal(target, hit.Value.Key);
    }

    [Fact]
    public void KeyCapGrid_RegisterHitsCoversEveryKey()
    {
        KeyCapGridGeometry geometry = KeyCapGrid.Measure(new Rect(0, 0, 1000, 400), KeyCapGrid.Layout65);
        HitLayer layer = new();

        KeyCapGrid.RegisterHits(layer, geometry);

        Assert.Equal(geometry.Keys.Count, layer.Count);
    }

    // ---------- CalendarGrid ----------

    [Fact]
    public void CalendarGrid_November2026StartsOnSunday()
    {
        CalendarGridGeometry geometry = CalendarGrid.MeasureMonth(
            new Rect(0, 0, 700, 600),
            2026,
            11,
            DayOfWeek.Monday);

        Assert.Equal(6, geometry.FirstDayOffset);
        Assert.Equal(30, geometry.DaysInMonth);
        Assert.Equal(6, geometry.Rows);
        Assert.Equal(30, geometry.Cells.Count);
        Assert.Equal(6, geometry.Cell(1)?.Column);
        Assert.Equal(5, geometry.Cell(30)?.Row);
        Assert.Equal(0, geometry.Cell(30)?.Column);
    }

    [Fact]
    public void CalendarGrid_SundayWeekStartRemovesOffset()
    {
        CalendarGridGeometry geometry = CalendarGrid.MeasureMonth(
            new Rect(0, 0, 700, 600),
            2026,
            11,
            DayOfWeek.Sunday);

        Assert.Equal(0, geometry.FirstDayOffset);
        Assert.Equal(0, geometry.Cell(1)?.Column);
    }

    [Fact]
    public void CalendarGrid_February2026NeedsFiveRows()
    {
        CalendarGridGeometry geometry = CalendarGrid.MeasureMonth(
            new Rect(0, 0, 700, 500),
            2026,
            2,
            DayOfWeek.Monday);

        Assert.Equal(6, geometry.FirstDayOffset);
        Assert.Equal(28, geometry.DaysInMonth);
        Assert.Equal(5, geometry.Rows);
        Assert.Equal(4, geometry.Cell(28)?.Row);
        Assert.Equal(5, geometry.Cell(28)?.Column);
    }

    [Fact]
    public void CalendarGrid_ExplicitOffsetControlsFirstCell()
    {
        CalendarGridGeometry geometry = CalendarGrid.Measure(new Rect(0, 0, 700, 400), 28, 0);

        Assert.Equal(0, geometry.FirstDayOffset);
        Assert.Equal(4, geometry.Rows);
        Assert.Equal(0, geometry.Cell(1)?.Row);
        Assert.Equal(0, geometry.Cell(1)?.Column);
    }

    [Fact]
    public void CalendarGrid_CellsStayInsideBounds()
    {
        CalendarGridGeometry geometry = CalendarGrid.MeasureMonth(new Rect(0, 0, 700, 600), 2026, 11);

        Assert.All(geometry.Cells, cell =>
        {
            Assert.True(cell.Bounds.Left >= -0.001);
            Assert.True(cell.Bounds.Top >= -0.001);
            Assert.True(cell.Bounds.Right <= 700.001);
            Assert.True(cell.Bounds.Bottom <= 600.001);
        });
    }

    [Fact]
    public void CalendarGrid_HitTestFindsDay()
    {
        CalendarGridGeometry geometry = CalendarGrid.MeasureMonth(new Rect(0, 0, 700, 600), 2026, 11);

        CalendarCell hit = Assert.IsType<CalendarCell>(geometry.HitTest(geometry.Cell(23)?.Bounds.Center ?? default));
        Assert.Equal(23, hit.Day);
    }

    [Fact]
    public void CalendarGrid_InvalidMonthThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalendarGrid.MeasureMonth(new Rect(0, 0, 700, 600), 2026, 13));
    }

    // ---------- TextBlock ----------

    [Theory]
    [InlineData("abcdef", 10, "abcdef")]
    [InlineData("abcdef", 6, "abcdef")]
    [InlineData("abcdef", 4, "abc…")]
    [InlineData("abcdef", 2, "a…")]
    [InlineData("abcdef", 1, "…")]
    [InlineData("abcdef", 0, "")]
    [InlineData("abcdef", 0.5, "")]
    [InlineData("你好世界", 3, "你好…")]
    public void TextBlock_EllipsisTruncatesByMeasure(string text, double maxWidth, string expected)
    {
        Assert.Equal(expected, TextBlock.Ellipsis(text, maxWidth, static value => value.Length));
    }

    [Fact]
    public void TextBlock_EllipsisPreservesTextElements()
    {
        static double Elements(string value) => StringInfo.ParseCombiningCharacters(value).Length;

        Assert.Equal("😀…", TextBlock.Ellipsis("😀abc", 2, Elements));
    }

    [Fact]
    public void TextBlock_EllipsisEmptyText()
    {
        Assert.Equal(string.Empty, TextBlock.Ellipsis(string.Empty, 10, static value => value.Length));
    }

    [Fact]
    public void TextBlock_EllipsisWithInfiniteWidthKeepsText()
    {
        Assert.Equal("abcdef", TextBlock.Ellipsis("abcdef", double.PositiveInfinity, static value => value.Length));
    }
}
