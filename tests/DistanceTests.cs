using System;
using System.IO;
using System.Reflection;
using System.Text;
using KeyMouseStats;

internal static class DistanceTests
{
    private static int _checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }
    private static void Near(double actual, double expected, string name)
    {
        Check(Math.Abs(actual - expected) < 1e-12, name);
    }
    private static void Put(byte[] bytes, int offset, int value)
    {
        Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
    }
    private static void Reset()
    {
        Store.History.Clear();
        Store.Total = new Counters();
        Store.MouseDpi = 0;
        Store.RollDay(DateTime.Today);
    }
    private static void Main()
    {
        Near(MouseDistance.ToMeters(800, 800), 0.0254, "800 DPI / one inch");
        Near(MouseDistance.ToMeters(1600, 1600), 0.0254, "1600 DPI / one inch");
        Near(MouseDistance.ToMeters(800, 800) + MouseDistance.ToMeters(1600, 1600), 0.0508, "DPI change preserves prior distance");
        Near(MouseDistance.CalibratedDpi(8000, 25.4), 800, "calibration");
        Check(!MouseDistance.ValidDpi(0) && !MouseDistance.ValidDpi(double.NaN)
            && !MouseDistance.ValidDpi(double.PositiveInfinity), "invalid DPI");
        Near(MouseDistance.ToMeters(800, 0), 0, "unset DPI does not accumulate");
        foreach (int pointerSize in new int[] { 4, 8 })
        {
            int header = 8 + 2 * pointerSize;
            byte[] packet = new byte[header + 24];
            Put(packet, 4, packet.Length);
            Put(packet, header + 12, -3);
            Put(packet, header + 16, 4);
            double counts; bool absolute;
            Check(RawMouseInput.Decode(packet, packet.Length, pointerSize, out counts, out absolute), "relative packet");
            Near(counts, 5, "signed diagonal");
            packet[header] = 8;
            Check(RawMouseInput.Decode(packet, packet.Length, pointerSize, out counts, out absolute), "no-coalesce flag accepted");
            packet[header] = 1;
            Check(!RawMouseInput.Decode(packet, packet.Length, pointerSize, out counts, out absolute) && absolute, "absolute input ignored");
            packet[header] = 0;
            Put(packet, header + 12, int.MinValue);
            Put(packet, header + 16, int.MaxValue);
            Check(RawMouseInput.Decode(packet, packet.Length, pointerSize, out counts, out absolute)
                && counts > int.MaxValue && !double.IsNaN(counts), "large deltas do not overflow");
            Put(packet, header + 12, 0);
            Put(packet, header + 16, 0);
            Check(RawMouseInput.Decode(packet, packet.Length, pointerSize, out counts, out absolute) && counts == 0, "button-only packet");
            Check(!RawMouseInput.Decode(packet, packet.Length - 1, pointerSize, out counts, out absolute), "truncated packet");
            Put(packet, 0, 1);
            Check(!RawMouseInput.Decode(packet, packet.Length, pointerSize, out counts, out absolute), "keyboard packet ignored");
        }

        // Redirect Store before any Load/Save: never access the user's statistics.
        string directory = Path.Combine(Path.GetTempPath(), "KeyMouseDistanceTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "stats.txt");
        Store.DataDirectory = directory;
        try
        {
            string date = DateTime.Today.ToString("yyyy-MM-dd");
            Reset();
            File.WriteAllText(file, "date=" + date + "\ntotal_move=1920\ntoday_move=960\n", Encoding.UTF8);
            Store.Load();
            Near(Store.Total.MovePx, 1920, "v1 total pixels preserved");
            Near(Store.Today.MovePx, 960, "v1 daily pixels preserved");
            Near(Store.Total.MoveMeters, 0, "v1 pixels not converted");
            Reset();
            File.WriteAllText(file, "date=" + date + "\ntotal_move=3840\n[day=" + date + "]\nmove=1920\n", Encoding.UTF8);
            Store.Load();
            Near(Store.Total.MovePx, 3840, "v2 total pixels preserved");
            Near(Store.Today.MovePx, 1920, "v2 daily pixels preserved");
            Near(Store.Today.MoveMeters, 0, "v2 pixels not converted");
            Near(Store.MouseDpi, 0, "migration does not assume DPI");
            Store.MouseDpi = 1600;
            Store.Today.MoveMeters = Store.Total.MoveMeters = MouseDistance.ToMeters(1, 1600);
            Store.Save();
            Check(File.Exists(file + ".bak") && File.ReadAllText(file + ".bak").Contains("total_move=3840"), "atomic replacement backup");
            Reset();
            Store.Load();
            Near(Store.MouseDpi, 1600, "DPI round trip");
            Near(Store.Total.MoveMeters, MouseDistance.ToMeters(1, 1600), "submillimeter precision");
            Near(Store.Today.MoveMeters, Store.Total.MoveMeters, "daily meters round trip");
            Near(Store.Today.MovePx, 1920, "v3 pixels round trip");
            Store.Today.MovePx = 0;
            Check(!Store.Today.IsEmpty, "movement-only day retained");
            Store.Save();
            Reset();
            Store.Load();
            Check(Store.Today.MoveMeters > 0, "movement-only day round trip");
            Check(Analysis.FmtMeters(0.0254) == "2.54 cm", "centimeter display");
            Check(Analysis.FmtMeters(15) == "15 m", "meter display");
            Check(Analysis.FmtMeters(1500) == "1.5 km", "kilometer display");
        }
        finally
        {
            foreach (string name in new string[] { "stats.txt", "stats.txt.tmp", "stats.txt.bak" })
            {
                string ownedFile = Path.Combine(directory, name);
                if (File.Exists(ownedFile)) File.Delete(ownedFile);
            }
            Directory.Delete(directory); // Empty test-owned directory only; no recursive deletion.
        }
        Console.WriteLine("PASS: " + _checks + " checks; process " + (IntPtr.Size * 8) + "-bit");
    }
}
