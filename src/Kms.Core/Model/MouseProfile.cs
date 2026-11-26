namespace Kms.Core.Model;

/// <summary>一只鼠标的独立磨损档案。对应 v3 <c>MouseProfile</c>。</summary>
public sealed class MouseProfile
{
    public const int DaySlots = 7;

    public string Id { get; set; } = "";
    public string Alias { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public string DevicePath { get; set; } = "";
    public string HardwareKey { get; set; } = "";
    public string InstanceKey { get; set; } = "";
    public bool BuiltIn { get; set; }
    public bool Legacy { get; set; }
    public bool Pending { get; set; }
    public bool Seeded { get; set; }
    public long RatedLeft { get; set; } = 80_000_000;
    public long RatedRight { get; set; } = 80_000_000;
    public DateTime Since { get; set; } = DateTime.MinValue;
    public long LastSeenUtcTicks { get; set; }

    public long Left { get; set; }
    public long Right { get; set; }
    public long Middle { get; set; }
    public long X { get; set; }

    public long[] DayStamp { get; } = new long[DaySlots];
    public long[] DayLeft { get; } = new long[DaySlots];
    public long[] DayRight { get; } = new long[DaySlots];
    public long[] DayMiddle { get; } = new long[DaySlots];
    public long[] DayX { get; } = new long[DaySlots];

    public long TotalClicks => Left + Right + Middle + X;

    public string DisplayName =>
        !string.IsNullOrEmpty(Alias) ? Alias :
        !string.IsNullOrEmpty(FriendlyName) ? FriendlyName :
        BuiltIn ? "笔记本内置触控板" :
        Legacy ? "旧版单鼠标" : "未命名鼠标";

    public void Count(int button, DateTime day)
    {
        if (button == 1)
        {
            Left++;
        }
        else if (button == 2)
        {
            Right++;
        }
        else if (button == 3)
        {
            Middle++;
        }
        else if (button >= 4)
        {
            X++;
        }

        RecordDay(button, day.Date);
    }

    private void RecordDay(int button, DateTime day)
    {
        long stamp = day.Ticks;
        int index = -1;
        for (int i = 0; i < DaySlots; i++)
        {
            if (DayStamp[i] == stamp)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            for (int i = 0; i < DaySlots - 1; i++)
            {
                DayStamp[i] = DayStamp[i + 1];
                DayLeft[i] = DayLeft[i + 1];
                DayRight[i] = DayRight[i + 1];
                DayMiddle[i] = DayMiddle[i + 1];
                DayX[i] = DayX[i + 1];
            }

            index = DaySlots - 1;
            DayStamp[index] = stamp;
            DayLeft[index] = 0;
            DayRight[index] = 0;
            DayMiddle[index] = 0;
            DayX[index] = 0;
        }

        if (button == 1)
        {
            DayLeft[index]++;
        }
        else if (button == 2)
        {
            DayRight[index]++;
        }
        else if (button == 3)
        {
            DayMiddle[index]++;
        }
        else if (button >= 4)
        {
            DayX[index]++;
        }
    }
}
