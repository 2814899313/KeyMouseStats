namespace Kms.Core.Model;

/// <summary>一把键盘的独立磨损档案。对应 v3 <c>KeyboardProfile</c>。</summary>
public sealed class KeyboardProfile
{
    public const int KeySlots = 256;

    public string Id { get; set; } = "";
    public string Alias { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public string DevicePath { get; set; } = "";
    public string HardwareKey { get; set; } = "";
    public string InstanceKey { get; set; } = "";
    public bool BuiltIn { get; set; }
    public bool Legacy { get; set; }
    public bool Pending { get; set; }
    public long Rated { get; set; } = 50_000_000;
    public DateTime Since { get; set; } = DateTime.MinValue;
    public bool Seeded { get; set; }
    public long LastSeenUtcTicks { get; set; }
    public long[] Keys { get; } = new long[KeySlots];

    public long TotalKeyPresses
    {
        get
        {
            long total = 0;
            for (int i = 0; i < KeySlots; i++)
            {
                total += Keys[i];
            }

            return total;
        }
    }

    public string DisplayName =>
        !string.IsNullOrEmpty(Alias) ? Alias :
        !string.IsNullOrEmpty(FriendlyName) ? FriendlyName :
        BuiltIn ? "笔记本内置键盘" :
        Legacy ? "旧版单键盘" : "未命名键盘";

    public void Reset(DateTime now)
    {
        Array.Clear(Keys);
        Since = now;
    }
}
