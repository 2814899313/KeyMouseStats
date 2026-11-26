using Kms.App.Reports;

namespace Kms.Golden.Tests;

/// <summary>报表窗 5 页整窗基线（940×640，默认主题）。</summary>
[Collection("page-golden")]
public sealed class ReportGoldenTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ReportTabs_MatchBaseline(int tab)
    {
        GoldenHarness.EnsureAvalonia();
        ReportWindow report = new(GoldenFixture.LoadStats());
        report.SelectTab(tab);
        byte[] actual = GoldenHarness.CaptureWindow(report);
        GoldenHarness.AssertPng($"report-tab-{tab}", actual);
    }
}
