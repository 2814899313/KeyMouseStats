using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Kms.Input.Devices;

/// <summary>
/// 一把键盘的稳定身份。移植自 v3 <c>KeyboardIdentity</c>。
/// 字段与 v3 一一对应，产品在后台解析完成后把它并入 <see cref="Kms.Core.Model.KeyboardProfile"/>。
/// </summary>
public sealed class KeyboardIdentity
{
    public string Id { get; set; } = "";

    public string HardwareKey { get; set; } = "";

    public string InstanceKey { get; set; } = "";

    public string DevicePath { get; set; } = "";

    public string FriendlyName { get; set; } = "";

    public bool BuiltIn { get; set; }
}

/// <summary>
/// 把 RawInput 的 hDevice 解析成稳定键盘身份：
/// <list type="bullet">
/// <item><c>GetRawInputDeviceInfo(RIDI_DEVICENAME)</c> 取接口路径；</item>
/// <item><c>ACPI\PNP0303</c> / <c>HID\INTC</c> / <c>HID\VID_8087</c> 等启发式识别内置键盘；</item>
/// <item><c>HID\VID_xxxx&amp;PID_xxxx</c> / <c>BTHENUM</c> 识别外接键盘；</item>
/// <item>读取注册表 <c>FriendlyName</c> / <c>DeviceDesc</c>，热路径只命中缓存。</item>
/// </list>
/// 所有解析失败都返回 <c>null</c> 或空串，绝不向输入线程抛异常；
/// 注册表负责把 <c>null</c> 落到「未知键盘」档案。
/// </summary>
public static class KeyboardDeviceIdentity
{
    private const uint RidiDeviceName = 0x20000007;
    private const int MaxDevicePathChars = 4096;

#pragma warning disable SYSLIB1054 // StringBuilder 输出缓冲区不能由 LibraryImport 生成。
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr device,
        uint command,
        StringBuilder? data,
        ref uint size);
#pragma warning restore SYSLIB1054

    /// <summary>
    /// 解析真实 RawInput 设备。失败返回 <c>null</c>，由注册表转成未知档案。
    /// </summary>
    public static KeyboardIdentity? Resolve(IntPtr hDevice)
    {
        string instance = NormalizePath(GetDevicePath(hDevice));
        if (instance.Length == 0)
        {
            return null;
        }

        return CreateNormalized(instance, ReadFriendlyName(instance));
    }

    /// <summary>
    /// 纯身份构造：给定设备接口路径与友好名，不访问 RawInput/注册表。
    /// 测试用它注入假注册表结果。
    /// </summary>
    public static KeyboardIdentity? Create(string? devicePath, string? friendlyName)
    {
        string instance = NormalizePath(devicePath);
        if (instance.Length == 0)
        {
            return null;
        }

        return CreateNormalized(instance, friendlyName);
    }

    private static KeyboardIdentity CreateNormalized(string instance, string? friendlyName)
    {
        string friendly = (friendlyName ?? "").Trim();
        bool builtIn = IsBuiltIn(instance, "");
        if (friendly.Length > 0 && IsBuiltIn(instance, friendly))
        {
            builtIn = true;
        }

        string hardware = HardwareKeyOf(instance);
        if (friendly.Length == 0)
        {
            friendly = FallbackName(hardware, builtIn);
        }

        return new KeyboardIdentity
        {
            DevicePath = instance,
            InstanceKey = instance,
            HardwareKey = hardware,
            FriendlyName = friendly,
            BuiltIn = builtIn,
            Id = (builtIn ? "builtin:" : "dev:")
                + Fnv(instance).ToString("X16", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// 读取 <c>RIDI_DEVICENAME</c>。调用失败、路径过长或返回空时给出空串。
    /// </summary>
    public static string GetDevicePath(IntPtr hDevice)
    {
        try
        {
            uint size = 0;
            uint first = GetRawInputDeviceInfo(hDevice, RidiDeviceName, null, ref size);
            if (first == uint.MaxValue || size == 0 || size > MaxDevicePathChars)
            {
                return "";
            }

            // size 是包含结尾 NUL 的字符数；StringBuilder 容量再留余量。
            StringBuilder buffer = new((int)size + 2);
            uint read = GetRawInputDeviceInfo(hDevice, RidiDeviceName, buffer, ref size);
            if (read == uint.MaxValue)
            {
                return "";
            }

            return buffer.ToString();
        }
        catch (Exception ex) when (ex is DllNotFoundException
                                   or EntryPointNotFoundException
                                   or MarshalDirectiveException
                                   or ArgumentException)
        {
            return "";
        }
    }

    /// <summary>
    /// 设备路径归一化：去掉 <c>\\?\</c>、<c>#</c> 转 <c>\</c>、折叠重复分隔符、
    /// 去掉末尾的接口 GUID，最后裁掉首尾分隔符。
    /// </summary>
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        string text = path.Trim();
        if (text.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            text = text[4..];
        }

        text = text.Replace('#', '\\');
        while (text.Contains(@"\\", StringComparison.Ordinal))
        {
            text = text.Replace(@"\\", @"\");
        }

        int last = text.LastIndexOf('\\');
        if (last > 0)
        {
            string tail = text[(last + 1)..];
            if (tail.Length >= 2 && tail[0] == '{' && tail[^1] == '}')
            {
                text = text[..last];
            }
        }

        return text.Trim('\\');
    }

    /// <summary>
    /// 硬件键：ACPI/BTHENUM/BTHHFENUM/HID 取前两段并大写，其余整串大写。
    /// 同型号设备共享硬件键，用于拔插后归并到既有档案。
    /// </summary>
    public static string HardwareKeyOf(string? instance)
    {
        if (string.IsNullOrEmpty(instance))
        {
            return "";
        }

        string[] parts = instance.Split('\\');
        if (parts.Length >= 2
            && (string.Equals(parts[0], "ACPI", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "BTHENUM", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[0], "BTHHFENUM", StringComparison.OrdinalIgnoreCase)))
        {
            return (parts[0] + "\\" + parts[1]).ToUpperInvariant();
        }

        if (parts.Length >= 2 && string.Equals(parts[0], "HID", StringComparison.OrdinalIgnoreCase))
        {
            return (parts[0] + "\\" + parts[1]).ToUpperInvariant();
        }

        return instance.ToUpperInvariant();
    }

    /// <summary>
    /// 内置键盘启发式。路径与友好名一起判定，友好名可补充路径识别不出的机型。
    /// </summary>
    public static bool IsBuiltIn(string? instance, string? friendly)
    {
        string text = ((instance ?? "") + " " + (friendly ?? "")).ToUpperInvariant();
        return text.Contains(@"ACPI\PNP0303", StringComparison.Ordinal)
            || text.Contains(@"ACPI\PNP030B", StringComparison.Ordinal)
            || text.Contains(@"ACPI\PNP0320", StringComparison.Ordinal)
            || text.Contains("I8042PRT", StringComparison.Ordinal)
            || text.Contains(@"HID\INTC", StringComparison.Ordinal)
            || text.Contains(@"HID\VID_8087", StringComparison.Ordinal)
            || text.Contains(@"HID\MSHW", StringComparison.Ordinal)
            || text.Contains("内置", StringComparison.Ordinal)
            || text.Contains("INTERNAL KEYBOARD", StringComparison.Ordinal);
    }

    /// <summary>
    /// 读取注册表友好名。资源间接字符串（<c>@%SystemRoot%\...</c>）不展开，避免阻塞热路径。
    /// </summary>
    public static string ReadFriendlyName(string? instance)
    {
        if (string.IsNullOrEmpty(instance))
        {
            return "";
        }

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\" + instance);
            if (key == null)
            {
                return "";
            }

            object? value = key.GetValue("FriendlyName") ?? key.GetValue("DeviceDesc");
            if (value is not string text || text.Length == 0)
            {
                return "";
            }

            if (text.StartsWith('@'))
            {
                return "";
            }

            return text.Trim();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or IOException
                                   or NotSupportedException)
        {
            return "";
        }
    }

    /// <summary>
    /// v3 的 FNV-1a：逐 UTF-16 字符异或后乘 64 位质数，不按 UTF-8 字节。
    /// 结果只用于稳定 Id，不参与安全判断。
    /// </summary>
    public static ulong Fnv(string? text)
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;
        ulong hash = Offset;
        if (text != null)
        {
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= Prime;
            }
        }

        return hash;
    }

    private static string FallbackName(string hardware, bool builtIn)
    {
        if (builtIn)
        {
            return "笔记本内置键盘";
        }

        if (!string.IsNullOrEmpty(hardware))
        {
            string[] parts = hardware.Split('\\');
            if (parts.Length >= 2 && parts[1].StartsWith("VID_", StringComparison.OrdinalIgnoreCase))
            {
                return "外接键盘 · " + parts[1].Replace('&', ' ');
            }
        }

        return "外接键盘";
    }
}
