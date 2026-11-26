using System.Runtime.InteropServices;
using System.Text;
using Kms.Core.Input;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Input;
using Kms.Input.Activity;
using Kms.Input.Counting;
using Kms.Input.Devices;
using Kms.Input.Hooks;
using Kms.Input.RawInput;
using Kms.Storage.Sqlite;

namespace InputProbe;

/// <summary>
/// 本机冒烟：安装真实低级钩子与 Raw Input，每秒打印计数。
/// 退出：Ctrl+C。只用于人工验证，不参与 CI。
/// </summary>
internal static class Program
{
    private const int WM_QUIT = 0x0012;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private static volatile bool _stop;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("input-probe：真实钩子 + Raw Input；Ctrl+C 退出。");

        int seconds = 0;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--seconds", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int parsed) && parsed > 0)
            {
                seconds = parsed;
            }
        }

        StatsData data = new();
        EventRing ring = new();
        CountingInputSink sink = new(data, ring, SystemClock.Instance, GetForegroundWindow);
        ActivityTracker tracker = new(date => data.GetOrCreate(date));
        ActivityMonitor monitor = new(tracker);
        using LowLevelHooks hooks = new(sink, busy: () => monitor.Busy);

        string dbPath = Path.Combine(Environment.CurrentDirectory, ".tmp-input-probe", "kmstats.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        using DbWriter writer = DbWriter.Open(dbPath, ring);
        writer.Start();

        RawDeviceSink rawSink = new();
        using RawInputHost rawHost = new(rawSink);
        bool rawStarted = rawHost.Start();
        bool hooksInstalled = hooks.Install();

        Console.WriteLine($"hooks={(hooksInstalled ? "ok" : "FAILED")} raw={(rawStarted ? "ok" : "FAILED")} db={dbPath}");

        uint threadId = GetCurrentThreadId();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            _stop = true;
            PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        };

        if (seconds > 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
                _stop = true;
                PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            });
        }

        using Timer timer = new(_ =>
        {
            monitor.Tick(60);
            Console.WriteLine(
                $"keys={sink.Keys} clicks={sink.Clicks} wheel={sink.WheelEvents} " +
                $"move={sink.MovePixels:0} rawMouse={rawHost.MousePacketCount} rawKb={rawHost.KeyboardPacketCount} " +
                $"active={data.History.Values.Sum(day => day.ActiveSeconds):0}s");
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        while (!_stop && GetMessage(out MSG message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        hooks.Uninstall();
        rawHost.Stop();
        writer.Flush(2_000);
        writer.Stop();
        Console.WriteLine($"stopped. lastEventMs={writer.LastEventMs} ringDropped={ring.DroppedCount}");
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private sealed class RawDeviceSink : IRawInputSink
    {
        public KeyboardDeviceRegistry Keyboards { get; } = new() { ResolveInline = true };
        public MouseDeviceRegistry Mice { get; } = new() { ResolveInline = true };

        public void RawMouse(IntPtr device, ushort buttonFlags, short wheelDelta, int dx, int dy)
        {
            Mice.HandleButtons(device, buttonFlags, DateTime.Now);
        }

        public void RawKey(IntPtr device, int virtualKey, int makeCode, uint flags, bool down)
        {
            Keyboards.HandleRawKey(device, virtualKey, makeCode, flags, down);
        }

        public void DeviceChanged(IntPtr device, bool removed)
        {
            if (removed)
            {
                Keyboards.OnDeviceRemoved(device);
                Mice.OnDeviceRemoved(device);
            }
        }
    }
}
