using Kms.Input.Activity;

namespace Kms.Input.Tests;

public sealed class ForegroundAppTests
{
    [Fact]
    public void Resolve_UsesInjectedResolverAndShortCache()
    {
        long now = 10_000;
        int calls = 0;
        ForegroundApp app = new(
            hwnd =>
            {
                calls++;
                return new WindowInfo(@"C:\tools\Code.exe", "Code.exe", "title-" + calls);
            },
            cacheCapacity: 8,
            cacheTtlMilliseconds: 1500,
            timestampMilliseconds: () => now);

        WindowInfo first = app.Resolve((IntPtr)1);
        WindowInfo second = app.Resolve((IntPtr)1);

        Assert.Equal(1, calls);
        Assert.Equal(first, second);
        Assert.Equal(@"C:\tools\Code.exe", first.ProcessPath);
        Assert.Equal("Code.exe", first.ProcessName);
        Assert.Equal("title-1", first.WindowTitle);

        now += 1500;
        Assert.Equal(first, app.Resolve((IntPtr)1));
        Assert.Equal(1, calls);

        now += 1;
        Assert.Equal("title-2", app.Resolve((IntPtr)1).WindowTitle);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Resolve_EvictsLeastRecentlyUsedEntry()
    {
        int calls = 0;
        ForegroundApp app = new(
            hwnd =>
            {
                calls++;
                return new WindowInfo(@"C:\app" + hwnd + ".exe", "", "w" + hwnd);
            },
            cacheCapacity: 2,
            cacheTtlMilliseconds: 60_000,
            timestampMilliseconds: () => 0);

        app.Resolve((IntPtr)1);
        app.Resolve((IntPtr)2);
        app.Resolve((IntPtr)1); // 重新触热 1
        app.Resolve((IntPtr)3); // 淘汰最久未用的 2

        Assert.Equal(3, calls);
        app.Resolve((IntPtr)1);
        Assert.Equal(3, calls);
        app.Resolve((IntPtr)2);
        Assert.Equal(4, calls);
    }

    [Fact]
    public void Resolve_ZeroWindowReturnsExplicitFallbackWithoutCallingResolver()
    {
        int calls = 0;
        ForegroundApp app = new(hwnd =>
        {
            calls++;
            return new WindowInfo("", "", "unexpected");
        });

        WindowInfo info = app.Resolve(IntPtr.Zero);

        Assert.Equal(0, calls);
        Assert.Equal(ForegroundApp.NoForegroundTitle, info.WindowTitle);
        Assert.Empty(info.ProcessPath);
        Assert.Empty(info.ProcessName);
    }

    [Fact]
    public void ReplacingResolverInvalidatesCacheImmediately()
    {
        int firstCalls = 0;
        int secondCalls = 0;
        ForegroundApp app = new(
            hwnd =>
            {
                firstCalls++;
                return new WindowInfo(@"C:\first.exe", "", "first");
            },
            timestampMilliseconds: () => 0);

        app.Resolve((IntPtr)42);
        Assert.Equal(1, firstCalls);

        app.ResolveWindowByHwnd = hwnd =>
        {
            secondCalls++;
            return new WindowInfo(@"C:\second.exe", "", "second");
        };

        Assert.Equal("second", app.Resolve((IntPtr)42).WindowTitle);
        Assert.Equal(1, firstCalls);
        Assert.Equal(1, secondCalls);
    }

    [Fact]
    public void InvalidateAndClearDropCachedEntries()
    {
        int calls = 0;
        ForegroundApp app = new(
            hwnd =>
            {
                calls++;
                return new WindowInfo("", "", "w" + hwnd);
            },
            timestampMilliseconds: () => 0);

        app.Resolve((IntPtr)1);
        app.Resolve((IntPtr)2);
        Assert.Equal(2, app.CachedWindowCount);

        Assert.True(app.Invalidate((IntPtr)1));
        Assert.False(app.Invalidate((IntPtr)1));
        Assert.Equal(1, app.CachedWindowCount);

        app.ClearWindowCache();
        Assert.Equal(0, app.CachedWindowCount);
        app.Resolve((IntPtr)2);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Capture_UsesInjectedForegroundSourceWithoutRealWindow()
    {
        int calls = 0;
        ForegroundApp app = new(
            hwnd =>
            {
                calls++;
                return new WindowInfo(@"C:\tools\Code.exe", "", "main.cs");
            },
            timestampMilliseconds: () => 0,
            foregroundWindow: () => (IntPtr)7);

        WindowInfo info = app.Capture();
        Assert.Equal(1, calls);
        Assert.Equal("main.cs", info.WindowTitle);
        Assert.Equal(@"C:\tools\Code.exe", info.ProcessPath);
    }

    [Fact]
    public void Capture_WhenForegroundChangesDuringResolve_ReturnsSwitchingFallback()
    {
        int reads = 0;
        ForegroundApp app = new(
            hwnd => new WindowInfo(@"C:\tools\Code.exe", "", "main.cs"),
            timestampMilliseconds: () => 0,
            foregroundWindow: () => reads++ == 0 ? (IntPtr)7 : (IntPtr)8);

        WindowInfo switched = app.Capture();

        Assert.Equal(ForegroundApp.SwitchingWindowTitle, switched.WindowTitle);
        Assert.Empty(switched.ProcessPath);
    }

    [Fact]
    public void Capture_NoForegroundDoesNotCallResolver()
    {
        int calls = 0;
        ForegroundApp app = new(
            hwnd =>
            {
                calls++;
                return new WindowInfo("", "", "unexpected");
            },
            foregroundWindow: () => IntPtr.Zero);

        WindowInfo info = app.Capture();

        Assert.Equal(0, calls);
        Assert.Equal(ForegroundApp.NoForegroundTitle, info.WindowTitle);
    }

    [Fact]
    public void WindowInfo_DefaultIsEmptyAndDerivesProcessName()
    {
        WindowInfo empty = default;
        Assert.Empty(empty.ProcessPath);
        Assert.Empty(empty.ProcessName);
        Assert.Empty(empty.WindowTitle);
        Assert.True(empty.IsEmpty);
        Assert.Equal(WindowInfo.Empty, empty);

        WindowInfo info = new(@"C:\Tools\Code.exe", "", "main.cs");
        Assert.Equal("Code.exe", info.ProcessName);
        Assert.False(info.IsEmpty);

        WindowInfo copy = new(@"C:\Tools\Code.exe", "Code.exe", "main.cs");
        Assert.Equal(info, copy);
        Assert.True(info == copy);
        Assert.False(info != copy);
    }
}
