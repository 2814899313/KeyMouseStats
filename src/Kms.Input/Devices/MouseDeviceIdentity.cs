using System.Globalization;

namespace Kms.Input.Devices;

/// <summary>
/// 一只鼠标的稳定身份。移植自 v3 <c>MouseIdentity</c>，字段与 v3 一一对应。
/// </summary>
public sealed class MouseIdentity
{
    public string Id { get; set; } = "";

    public string HardwareKey { get; set; } = "";

    public string InstanceKey { get; set; } = "";

    public string DevicePath { get; set; } = "";

    public string FriendlyName { get; set; } = "";

    public bool BuiltIn { get; set; }
}

/// <summary>
/// 鼠标 RawInput 设备身份：
/// <list type="bullet">
/// <item>复用键盘的路径归一化 / 注册表友好名查询，不重复阻塞热路径；</item>
/// <item>ACPI / Synaptics / Elan / Intel HID 等启发式识别内置触控板，其余按外接鼠标；</item>
/// <item>解析失败返回 <c>null</c>，由注册表落到「未知鼠标」档案。</item>
/// </list>
/// </summary>
public static class MouseDeviceIdentity
{
    /// <summary>解析真实 RawInput 设备。失败返回 <c>null</c>，绝不抛。</summary>
    public static MouseIdentity? Resolve(IntPtr hDevice)
    {
        string instance = KeyboardDeviceIdentity.NormalizePath(
            KeyboardDeviceIdentity.GetDevicePath(hDevice));
        if (instance.Length == 0)
        {
            return null;
        }

        return CreateNormalized(instance, KeyboardDeviceIdentity.ReadFriendlyName(instance));
    }

    /// <summary>
    /// 纯身份构造：给定设备接口路径与友好名，不访问 RawInput/注册表。
    /// 测试用它注入假注册表结果。
    /// </summary>
    public static MouseIdentity? Create(string? devicePath, string? friendlyName)
    {
        string instance = KeyboardDeviceIdentity.NormalizePath(devicePath);
        if (instance.Length == 0)
        {
            return null;
        }

        return CreateNormalized(instance, friendlyName);
    }

    private static MouseIdentity CreateNormalized(string instance, string? friendlyName)
    {
        string friendly = (friendlyName ?? "").Trim();
        bool builtIn = IsBuiltIn(instance, friendly);
        string hardware = KeyboardDeviceIdentity.HardwareKeyOf(instance);
        if (friendly.Length == 0)
        {
            friendly = FallbackName(hardware, builtIn);
        }

        return new MouseIdentity
        {
            DevicePath = instance,
            InstanceKey = instance,
            HardwareKey = hardware,
            FriendlyName = friendly,
            BuiltIn = builtIn,
            Id = "mouse:" + (builtIn ? "builtin:" : "dev:")
                + KeyboardDeviceIdentity.Fnv(instance).ToString("X16", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// 内置触控板启发式。注意 USB 触控板常见 Synaptics (06CB) / Elan (04F3) /
    /// Intel HID (8087) 三个 VID，不能只靠 ACPI 判定。
    /// </summary>
    public static bool IsBuiltIn(string? instance, string? friendly)
    {
        string text = ((instance ?? "") + " " + (friendly ?? "")).ToUpperInvariant();
        return text.Contains(@"ACPI\", StringComparison.Ordinal)
            || text.Contains("I8042PRT", StringComparison.Ordinal)
            || text.Contains(@"HID\INTC", StringComparison.Ordinal)
            || text.Contains(@"HID\MSHW", StringComparison.Ordinal)
            || text.Contains("VID_06CB", StringComparison.Ordinal)
            || text.Contains("VID_04F3", StringComparison.Ordinal)
            || text.Contains("VID_8087", StringComparison.Ordinal)
            || text.Contains("TOUCHPAD", StringComparison.Ordinal)
            || text.Contains("TOUCH PAD", StringComparison.Ordinal)
            || text.Contains("TRACKPAD", StringComparison.Ordinal)
            || text.Contains("触控板", StringComparison.Ordinal)
            || text.Contains("触摸板", StringComparison.Ordinal);
    }

    private static string FallbackName(string hardware, bool builtIn)
    {
        if (builtIn)
        {
            return "笔记本触控板";
        }

        if (!string.IsNullOrEmpty(hardware))
        {
            string[] parts = hardware.Split('\\');
            if (parts.Length >= 2 && parts[1].StartsWith("VID_", StringComparison.OrdinalIgnoreCase))
            {
                return "外接鼠标 · " + parts[1].Replace('&', ' ');
            }
        }

        return "外接鼠标";
    }
}
