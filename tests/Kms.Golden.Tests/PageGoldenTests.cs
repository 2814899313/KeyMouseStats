using Avalonia.Controls;
using Kms.App.Pages;
using Kms.Core.Model;
using Kms.Core.Text;
using Kms.Storage.Text;

namespace Kms.Golden.Tests;

/// <summary>
/// 9 页主面板的整页基线（96 DPI，928×730，对应 1180 主窗去掉侧栏后的内容区）。
/// 基线缺失或设置 KMS_UPDATE_GOLDEN=1 时写入，人工确认后再作为回归基线。
/// </summary>
[Collection("page-golden")]
public sealed class PageGoldenTests
{
    private const int Width = 928;
    private const int Height = 730;

    [Theory]
    [InlineData("page-overview")]
    [InlineData("page-trend")]
    [InlineData("page-hours")]
    [InlineData("page-keys")]
    [InlineData("page-apps")]
    [InlineData("page-insights")]
    [InlineData("page-text")]
    [InlineData("page-day")]
    [InlineData("page-heat")]
    public void Pages_MatchBaseline(string name)
    {
        StatsData data = LoadFixture();
        string repo = GoldenFixture.RepoRoot();
        RimeLog.PathOverride = name == "page-text"
            ? Path.Combine(repo, "tests", "Fixtures", "rime-log-sample.csv")
            : Path.Combine(Path.GetTempPath(), "kms-no-rime-log.csv");
        try
        {
            Control page = name switch
            {
                "page-overview" => new OverviewPage(data, "总览", "今天与最近 7 天的整体节奏"),
                "page-trend" => new TrendPage(data, "趋势", "选择区间与基线，查看变化"),
                "page-hours" => new HoursPage(data, "时段分布", "24 小时内的输入与活跃时长"),
                "page-keys" => new KeysPage(data, "按键排行", "排行、按住时长、组合键与配列"),
                "page-apps" => new AppsPage(data, "应用统计", "前台窗口、APM 与时长占比"),
                "page-insights" => new InsightsPage(data, "使用洞察", "专注段、上下文切换与时光机"),
                "page-text" => new TextPage("字符内容", "Rime 上屏文字与词频"),
                "page-day" => new DayPage(data, "日报", "单日摘要与可导出长图"),
                _ => new HeatPage(data, "点击热区", "点击位置与三通道分布")
            };

            GoldenHarness.AssertMatches(name, page, Width, Height, hostInWindow: true);
        }
        finally
        {
            RimeLog.PathOverride = null;
        }
    }

    [Theory]
    [InlineData(96, "page-hours-96")]
    [InlineData(144, "page-hours-144")]
    [InlineData(192, "page-hours-192")]
    public void Hours_ThreeDpi_MatchesBaseline(double dpi, string name)
    {
        StatsData data = LoadFixture();
        HoursPage page = new(data, "小时分布", "24 小时内的输入与活跃时长");
        GoldenHarness.AssertMatches(name, page, Width, Height, dpi, hostInWindow: true);
    }

    [Fact]
    public void Text_Compact_MatchesBaseline()
    {
        StatsData data = LoadFixture();
        string repo = GoldenFixture.RepoRoot();
        RimeLog.PathOverride = Path.Combine(repo, "tests", "Fixtures", "rime-log-sample.csv");
        try
        {
            TextPage page = new("字符内容", "Rime 上屏文字与词频");
            GoldenHarness.AssertMatches("page-text-compact", page, Width, 650, hostInWindow: true);
        }
        finally
        {
            RimeLog.PathOverride = null;
        }
    }

    [Fact]
    public void Keys_Compact_MatchesBaseline()
    {
        StatsData data = LoadFixture();
        KeysPage page = new(data, "按键分析", "排行、按住时长、组合键与配列");
        GoldenHarness.AssertMatches("page-keys-compact", page, Width, 650, hostInWindow: true);
    }

    private static StatsData LoadFixture() => GoldenFixture.LoadStats();
}

[CollectionDefinition("page-golden", DisableParallelization = true)]
public sealed class PageGoldenCollection;
