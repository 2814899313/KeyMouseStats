using System.Diagnostics;
using Kms.Core.Model;
using Kms.Core.Stats;
using Kms.Storage.Text;

namespace Kms.Benchmarks;

/// <summary>
/// 纯逻辑基准：v13 解析/保存与 FlowAnalyzer。UI 帧时间请跑
/// <c>Kms.App --bench-ui</c>（真实窗口 + 合成器）。
/// </summary>
internal static class Program
{
    private static int Main()
    {
        string root = RepoRoot();
        string fixture = Path.Combine(root, "tests", "Fixtures", "v13-text-current.txt");
        if (!File.Exists(fixture))
        {
            Console.Error.WriteLine($"找不到固件：{fixture}");
            return 1;
        }

        string text = File.ReadAllText(fixture);
        StatsData data = TextStatsCodec.Load(text);

        Measure("TextStatsCodec.Load", 20, () => TextStatsCodec.Load(text));
        Measure("TextStatsCodec.Save", 20, () => TextStatsCodec.Save(data));

        List<List<FlowSessionRow>> daily = data.History.Values
            .Select(day => ToFlowRows(day))
            .ToList();
        Measure("FlowAnalyzer.Analyze × 全部日", 20, () =>
        {
            foreach (List<FlowSessionRow> rows in daily)
            {
                FlowAnalyzer.Analyze(rows, null, null, null);
            }
        });

        Console.WriteLine();
        Console.WriteLine("UI 帧时间：dotnet src\\Kms.App\\bin\\Release\\net10.0-windows\\Kms.App.dll --bench-ui");
        return 0;
    }

    private static void Measure(string name, int iterations, Action action)
    {
        action();
        double[] samples = new double[iterations];
        for (int i = 0; i < iterations; i++)
        {
            Stopwatch watch = Stopwatch.StartNew();
            action();
            watch.Stop();
            samples[i] = watch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        Console.WriteLine(
            $"{name,-34} avg={samples.Average(),8:0.000} ms  p50={samples[iterations / 2],8:0.000}  " +
            $"p95={samples[Math.Min(iterations - 1, (int)(iterations * 0.95))],8:0.000}  " +
            $"min={samples[0],8:0.000}  max={samples[^1],8:0.000}");
    }

    private static List<FlowSessionRow> ToFlowRows(DayRecord day)
    {
        List<FlowSessionRow> rows = [];
        long id = 1;
        foreach (ActiveSession session in day.Sessions)
        {
            rows.Add(new FlowSessionRow
            {
                Id = id++,
                StartMs = new DateTimeOffset(DateTime.SpecifyKind(session.Start, DateTimeKind.Local)).ToUnixTimeMilliseconds(),
                EndMs = new DateTimeOffset(DateTime.SpecifyKind(session.End, DateTimeKind.Local)).ToUnixTimeMilliseconds(),
                ProcessPath = session.AppSeconds.Keys.FirstOrDefault() ?? "",
                ProcessName = Path.GetFileName(session.AppSeconds.Keys.FirstOrDefault() ?? ""),
                KeyCount = day.Keys,
                InputCount = day.Keys,
                CategoryId = 5,
                ActiveMs = (long)(session.Seconds * 1000)
            });
        }

        return rows;
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeyMouseStats.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录。");
    }
}
