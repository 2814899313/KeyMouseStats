using Kms.Storage.Package;

namespace Kms.Storage.Tests;

public sealed class KmsPackageTests
{
    private static KmsPackageData Sample() => new()
    {
        Version = 1,
        DeviceId = "machine\tA",
        DayRange = "2026-11-01..2026-11-22",
        Days =
        {
            new KmsDayRecord { Day = "2026-11-22", Keys = 10, Clicks = 3, Wheel = 2, ActiveSeconds = 100 },
            new KmsDayRecord { Day = "2026-11-21", Keys = 20, Clicks = 4, Wheel = 5, ActiveSeconds = 200 }
        },
        Sessions =
        {
            new KmsSessionRecord
            {
                Day = "2026-11-22", StartMs = 1000, EndMs = 2000, KeyCount = 9,
                CategoryId = 1, ProcessPath = @"C:\a\b.exe", ProcessName = "b.exe",
                WindowTitle = "line1\nline2\ttab"
            }
        }
    };

    [Fact]
    public void ExportImport_RoundTripsAndEscapes()
    {
        KmsPackageData source = Sample();
        string text = KmsPackage.Export(source);
        KmsPackageData parsed = KmsPackage.Import(text);

        Assert.Equal(1, parsed.Version);
        Assert.Equal("machine\tA", parsed.DeviceId);
        Assert.Equal(2, parsed.Days.Count);
        Assert.Single(parsed.Sessions);
        Assert.Equal("line1\nline2\ttab", parsed.Sessions[0].WindowTitle);
        Assert.Equal(source.Sessions[0].RecordKey, parsed.Sessions[0].RecordKey);
    }

    [Fact]
    public void Import_RejectsTamperedPayload()
    {
        string text = KmsPackage.Export(Sample()).Replace("D\t2026-11-22\t10", "D\t2026-11-22\t11", StringComparison.Ordinal);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => KmsPackage.Import(text));
        Assert.Contains("校验和", error.Message);
    }

    [Fact]
    public void Merge_IsIdempotentPerRecordKey()
    {
        KmsPackageData data = Sample();
        Dictionary<string, string> ledger = [];
        List<KmsDayRecord> days = [];
        List<KmsSessionRecord> sessions = [];

        KmsMergeResult first = KmsPackage.Merge(data, ledger, days, sessions);
        KmsMergeResult second = KmsPackage.Merge(data, ledger, days, sessions);

        Assert.True(first.Changed);
        Assert.Equal(2, first.DaysApplied);
        Assert.Equal(1, first.SessionsApplied);
        Assert.False(second.Changed);
        Assert.Equal(2, days.Count);
        Assert.Single(sessions);
    }

    [Fact]
    public void Fingerprint_IsFirst16HexCharacters()
    {
        string fingerprint = KmsPackage.Fingerprint(@"C:\a\b.exe");

        Assert.Equal(16, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{16}$", fingerprint);
    }
}
