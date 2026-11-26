using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kms.Core.Stats;
using Kms.Render.Motion;

namespace Kms.App.Diagnostics;

/// <summary>
/// --bench-ui：自动翻 9 页并强制重绘，记录真实合成器的帧间隔，输出 JSON。
/// 不启动 InputService（不挂钩子），因此可以与正在运行的采集实例并存。
/// </summary>
internal static class UiBenchmark
{
    private const double PageSeconds = 2.0;
    private const double TotalSeconds = 18.0;

    public static void Run(
        MainWindow main,
        IClassicDesktopStyleApplicationLifetime desktop,
        Action? beforeShutdown = null)
    {
        DateTime start = DateTime.UtcNow;
        DateTime pageStart = start;
        DateTime last = start;
        int page = 0;
        int frames = 0;
        List<double> all = [];
        List<double>[] perPage = Enumerable.Range(0, 9).Select(_ => new List<double>()).ToArray();
        FrameBudget budget = new();

        void Finish(string reason)
        {
            BenchResult result = new()
            {
                Timestamp = DateTimeOffset.Now,
                Machine = Environment.MachineName,
                Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                RenderScaling = main.RenderScaling,
                PageSeconds = PageSeconds,
                Frames = frames,
                Reason = reason,
                TargetFps = budget.TargetFps,
                Overall = Stats(all),
                Pages = perPage.Select((list, index) => new PageStats
                {
                    Page = index,
                    Frames = list.Count,
                    Stats = Stats(list)
                }).ToArray()
            };
            string path = Path.Combine(Path.GetTempPath(), "kms-v4-bench.json");
            File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            beforeShutdown?.Invoke();
            desktop.Shutdown();
        }

        void NextFrame(TimeSpan timestamp)
        {
            if (main.IsVisible)
            {
                double delta = (DateTime.UtcNow - last).TotalMilliseconds;
                last = DateTime.UtcNow;
                if (delta > 0 && delta < 200)
                {
                    frames++;
                    all.Add(delta);
                    perPage[page].Add(delta);
                    budget.Sample(TimeSpan.FromMilliseconds(delta));
                }

                if ((DateTime.UtcNow - pageStart).TotalSeconds >= PageSeconds && page < 8)
                {
                    page++;
                    pageStart = DateTime.UtcNow;
                    main.Shell.SelectPage(page);
                }

                main.Shell.InvalidateVisual();
            }

            if ((DateTime.UtcNow - start).TotalSeconds >= TotalSeconds)
            {
                Finish("completed");
                return;
            }

            main.RequestAnimationFrame(NextFrame);
        }

        main.RequestAnimationFrame(NextFrame);
        DispatcherTimer.RunOnce(
            () =>
            {
                if (main.IsVisible)
                {
                    Finish("timeout");
                }
            },
            TimeSpan.FromSeconds(TotalSeconds + 15));
    }

    private static FrameStats Stats(List<double> values)
    {
        if (values.Count == 0)
        {
            return new FrameStats();
        }

        List<double> sorted = [.. values.OrderBy(value => value)];
        double avg = sorted.Average();
        return new FrameStats
        {
            AvgMs = Math.Round(avg, 3),
            P50Ms = Math.Round(Percentile(sorted, 0.50), 3),
            P95Ms = Math.Round(Percentile(sorted, 0.95), 3),
            P99Ms = Math.Round(Percentile(sorted, 0.99), 3),
            MinMs = Math.Round(sorted[0], 3),
            MaxMs = Math.Round(sorted[^1], 3),
            AvgFps = Math.Round(1000.0 / Math.Max(0.001, avg), 2)
        };
    }

    private static double Percentile(List<double> sorted, double p)
    {
        double position = (sorted.Count - 1) * p;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sorted[lower]
            : sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private sealed class BenchResult
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Machine { get; set; } = "";
        public string Os { get; set; } = "";
        public double RenderScaling { get; set; }
        public double PageSeconds { get; set; }
        public int Frames { get; set; }
        public string Reason { get; set; } = "";
        public int TargetFps { get; set; }
        public FrameStats Overall { get; set; } = new();
        public PageStats[] Pages { get; set; } = [];
    }

    private sealed class PageStats
    {
        public int Page { get; set; }
        public int Frames { get; set; }
        public FrameStats Stats { get; set; } = new();
    }

    private sealed class FrameStats
    {
        public double AvgMs { get; set; }
        public double P50Ms { get; set; }
        public double P95Ms { get; set; }
        public double P99Ms { get; set; }
        public double MinMs { get; set; }
        public double MaxMs { get; set; }
        public double AvgFps { get; set; }
    }
}
