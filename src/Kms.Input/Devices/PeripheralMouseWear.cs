using System.Globalization;
using System.Text;
using Kms.Core.Model;

namespace Kms.Input.Devices;

/// <summary>
/// 多鼠标磨损档案：
/// <list type="bullet">
/// <item>每只鼠标独立 Id / Alias / FriendlyName / RatedLeft / RatedRight / Since /
/// 左/右/中/侧键计数 / 近 7 天点击环；</item>
/// <item>RawInput 按 hDevice 路由；低级钩子只在原始输入不可用时回退 unknown；</item>
/// <item>旧版单鼠标保留为 legacy 档案，可一键归入当前鼠标。</item>
/// </list>
/// 热路径只做计数与 7 槽日期查找，不做 I/O。
/// </summary>
public sealed class PeripheralMouseWear
{
    public const string PrimaryId = "mprimary";
    public const string LegacyId = "mlegacy";
    public const string UnknownId = "munknown";

    private readonly object _gate = new();
    private readonly List<MouseProfile> _mice = new();
    private string _activeId = "";
    private bool _loaded;
    private bool _needSeed = true;

    /// <summary>产品默认实例；测试应各自 <c>new PeripheralMouseWear()</c>。</summary>
    public static PeripheralMouseWear Shared { get; } = new();

    /// <summary>首次加载后是否还需要从历史记录回填点击计数。</summary>
    public bool NeedsSeed
    {
        get
        {
            lock (_gate)
            {
                return !_loaded || _needSeed;
            }
        }
    }

    public string ActiveMouseId
    {
        get
        {
            lock (_gate)
            {
                return _activeId;
            }
        }
    }

    public MouseProfile PrimaryMouse
    {
        get
        {
            lock (_gate)
            {
                return FindPrimaryLocked() ?? EnsureDefaultLocked();
            }
        }
    }

    public static bool IsPlaceholder(MouseProfile? profile)
    {
        return profile is { Pending: false, Legacy: false, Id: PrimaryId }
            && string.Equals(profile.Alias ?? "", "主力鼠标", StringComparison.Ordinal)
            && string.IsNullOrEmpty(profile.FriendlyName)
            && profile.TotalClicks == 0;
    }

    public MouseProfile? Find(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        lock (_gate)
        {
            return FindLocked(id);
        }
    }

    public List<MouseProfile> SnapshotMice(bool includePending = false)
    {
        lock (_gate)
        {
            List<MouseProfile> list = new(_mice.Count);
            foreach (MouseProfile profile in _mice)
            {
                if (!includePending && profile.Pending)
                {
                    continue;
                }

                if (IsPlaceholder(profile))
                {
                    continue;
                }

                list.Add(profile);
            }

            return list;
        }
    }

    public void SetActiveProfile(MouseProfile? profile)
    {
        if (profile == null)
        {
            return;
        }

        lock (_gate)
        {
            _activeId = profile.Id;
        }

        profile.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
    }

    // ---------------------------------------------------------------- 主档案兼容

    public string MouseName
    {
        get => PrimaryMouse.DisplayName;
        set
        {
            MouseProfile profile = PrimaryMouse;
            profile.Alias = value ?? "";
            profile.Pending = false;
            profile.Seeded = true;
        }
    }

    public long MouseRatedLeft
    {
        get => PrimaryMouse.RatedLeft;
        set
        {
            MouseProfile profile = PrimaryMouse;
            profile.RatedLeft = PeripheralWear.ClampRated(value, PeripheralWear.DefaultMouseRated);
        }
    }

    public long MouseRatedRight
    {
        get => PrimaryMouse.RatedRight;
        set
        {
            MouseProfile profile = PrimaryMouse;
            profile.RatedRight = PeripheralWear.ClampRated(value, PeripheralWear.DefaultMouseRated);
        }
    }

    public DateTime MouseSince
    {
        get => PrimaryMouse.Since;
        set => PrimaryMouse.Since = value;
    }

    public bool Seeded
    {
        get => PrimaryMouse.Seeded;
        set => PrimaryMouse.Seeded = value;
    }

    public long LeftClicks => Read(PrimaryMouse, 1);

    public long RightClicks => Read(PrimaryMouse, 2);

    public long MiddleClicks => Read(PrimaryMouse, 3);

    public long XButtons => Read(PrimaryMouse, 4);

    public long TotalClicksAll
    {
        get
        {
            long total = 0;
            foreach (MouseProfile profile in SnapshotMice(false))
            {
                total += profile.TotalClicks;
            }

            return total;
        }
    }

    // ---------------------------------------------------------------- 计数

    public void CountClick(MouseProfile? profile, int button, DateTime now)
    {
        profile ??= UnknownProfile;
        profile.Count(button, now);
    }

    /// <summary>兼容旧 API：计入主鼠标，时间取当前本地时间。</summary>
    public void CountClick(int button)
    {
        PrimaryMouse.Count(button, DateTime.Now);
    }

    public void CountUnknown(int button, DateTime now)
    {
        UnknownProfile.Count(button, now);
    }

    public static long Read(MouseProfile? profile, int button)
    {
        if (profile == null)
        {
            return 0;
        }

        return button switch
        {
            1 => profile.Left,
            2 => profile.Right,
            3 => profile.Middle,
            _ => profile.X
        };
    }

    /// <summary>读取主鼠标的按钮计数（1=左 2=右 3=中 其他=侧键）。</summary>
    public long Read(int button)
    {
        return Read(PrimaryMouse, button);
    }

    public static void CopyDaysTo(MouseProfile profile, long[] target)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(target);
        int count = Math.Min(target.Length, MouseProfile.DaySlots);
        for (int i = 0; i < count; i++)
        {
            target[i] = profile.DayLeft[i] + profile.DayRight[i] + profile.DayMiddle[i] + profile.DayX[i];
        }
    }

    public static double WorstLeft(MouseProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return PeripheralWear.Ratio(profile.Left, profile.RatedLeft);
    }

    public static double WorstRight(MouseProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return PeripheralWear.Ratio(profile.Right, profile.RatedRight);
    }

    public double WorstRatio()
    {
        double worst = 0;
        foreach (MouseProfile profile in SnapshotMice(false))
        {
            worst = Math.Max(worst, WorstLeft(profile));
            worst = Math.Max(worst, WorstRight(profile));
        }

        return worst;
    }

    // ---------------------------------------------------------------- 档案

    public MouseProfile EnsureDefault()
    {
        lock (_gate)
        {
            if (_mice.Count == 0)
            {
                return EnsureDefaultLocked();
            }

            return FindPrimaryLocked() ?? EnsureDefaultLocked();
        }
    }

    public MouseProfile UnknownProfile
    {
        get
        {
            lock (_gate)
            {
                MouseProfile? profile = FindLocked(UnknownId);
                if (profile != null)
                {
                    return profile;
                }

                profile = new MouseProfile
                {
                    Id = UnknownId,
                    Alias = "未知鼠标",
                    FriendlyName = "RawInput 不可用时的回退档案",
                    Seeded = true
                };
                _mice.Add(profile);
                return profile;
            }
        }
    }

    public MouseProfile CreatePendingProfile(IntPtr handle)
    {
        MouseProfile profile = new()
        {
            Id = "mouse-pending:" + handle.ToInt64().ToString("X", CultureInfo.InvariantCulture),
            Alias = "识别中…",
            Pending = true,
            Seeded = true,
            Since = DateTime.Today
        };
        lock (_gate)
        {
            _mice.Add(profile);
        }

        return profile;
    }

    public MouseProfile? ResolvePending(MouseProfile? pending, MouseIdentity? identity)
    {
        if (pending == null)
        {
            return null;
        }

        lock (_gate)
        {
            if (!pending.Pending)
            {
                return pending;
            }

            MouseProfile? blank = FindLocked(PrimaryId);
            if (blank != null && blank != pending && IsPlaceholder(blank))
            {
                _mice.Remove(blank);
            }

            if (identity == null)
            {
                MouseProfile? unknown = FindLocked(UnknownId);
                if (unknown != null && unknown != pending)
                {
                    AddClicks(pending, unknown);
                    _mice.Remove(pending);
                    return unknown;
                }

                pending.Id = UnknownId;
                pending.Alias = "未知鼠标";
                pending.FriendlyName = "RawInput 不可用时的回退档案";
                pending.Pending = false;
                _activeId = pending.Id;
                return pending;
            }

            MouseProfile? target = null;
            foreach (MouseProfile profile in _mice)
            {
                if (profile == pending || profile.Pending)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(identity.InstanceKey)
                    && string.Equals(profile.InstanceKey, identity.InstanceKey, StringComparison.OrdinalIgnoreCase))
                {
                    target = profile;
                    break;
                }
            }

            if (target == null && !string.IsNullOrEmpty(identity.HardwareKey))
            {
                int matches = 0;
                MouseProfile? only = null;
                foreach (MouseProfile profile in _mice)
                {
                    if (profile == pending || profile.Pending)
                    {
                        continue;
                    }

                    if (string.Equals(profile.HardwareKey, identity.HardwareKey, StringComparison.OrdinalIgnoreCase))
                    {
                        matches++;
                        only = profile;
                    }
                }

                if (matches == 1)
                {
                    target = only;
                }
            }

            if (target == null)
            {
                MouseProfile? sameId = FindLocked(identity.Id);
                if (sameId != null && sameId != pending)
                {
                    target = sameId;
                    AddClicks(pending, target);
                    _mice.Remove(pending);
                }
                else
                {
                    target = pending;
                    pending.Id = identity.Id;
                    pending.Pending = false;
                    pending.Alias = "";
                }
            }
            else
            {
                AddClicks(pending, target);
                _mice.Remove(pending);
            }

            target.FriendlyName = identity.FriendlyName ?? "";
            target.DevicePath = identity.DevicePath ?? "";
            target.HardwareKey = identity.HardwareKey ?? "";
            target.InstanceKey = identity.InstanceKey ?? "";
            target.BuiltIn = identity.BuiltIn;
            target.Legacy = false;
            target.Seeded = true;
            target.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
            _activeId = target.Id;
            return target;
        }
    }

    public bool MergeLegacyInto(string targetId)
    {
        lock (_gate)
        {
            MouseProfile? legacy = FindLocked(LegacyId);
            MouseProfile? target = FindLocked(targetId);
            if (legacy == null || target == null || legacy == target)
            {
                return false;
            }

            AddClicks(legacy, target);
            if (target.Since == DateTime.MinValue)
            {
                target.Since = legacy.Since;
            }

            _mice.Remove(legacy);
            return true;
        }
    }

    // ---------------------------------------------------------------- 重置

    public void Reset(DateTime now)
    {
        ResetProfile(PrimaryMouse, now);
    }

    public void Reset(string id, DateTime now)
    {
        MouseProfile? profile = Find(id);
        if (profile != null)
        {
            ResetProfile(profile, now);
        }
    }

    /// <summary>设置主鼠标的名称/左右额定/起始日期；空名称保持原别名。</summary>
    public void ApplySettings(string? name, long ratedLeft, long ratedRight, DateTime since)
    {
        MouseProfile profile = PrimaryMouse;
        if (!string.IsNullOrEmpty(name))
        {
            profile.Alias = name;
        }

        profile.RatedLeft = PeripheralWear.ClampRated(ratedLeft, PeripheralWear.DefaultMouseRated);
        profile.RatedRight = PeripheralWear.ClampRated(ratedRight, PeripheralWear.DefaultMouseRated);
        profile.Since = since;
    }

    // ---------------------------------------------------------------- 加载 / 落盘

    public void BeginLoad()
    {
        lock (_gate)
        {
            _mice.Clear();
            _activeId = "";
        }

        _needSeed = true;
        _loaded = false;
    }

    public void ResetForTest()
    {
        BeginLoad();
        lock (_gate)
        {
            EnsureDefaultLocked();
        }
    }

    public void LoadProfiles(IEnumerable<MouseProfile>? profiles)
    {
        lock (_gate)
        {
            _mice.Clear();
            if (profiles != null)
            {
                foreach (MouseProfile profile in profiles)
                {
                    if (string.IsNullOrEmpty(profile.Id) || FindLocked(profile.Id) != null)
                    {
                        continue;
                    }

                    _mice.Add(profile);
                }
            }

            if (string.IsNullOrEmpty(_activeId) || FindLocked(_activeId) == null)
            {
                foreach (MouseProfile profile in _mice)
                {
                    if (!profile.Pending)
                    {
                        _activeId = profile.Id;
                        break;
                    }
                }
            }
        }

        _needSeed = false;
        _loaded = true;
    }

    public void LoadProfiles(
        IEnumerable<string>? headers,
        IEnumerable<string>? buttonLines,
        IEnumerable<string>? dayLines)
    {
        lock (_gate)
        {
            _mice.Clear();
            if (headers != null)
            {
                foreach (string header in headers)
                {
                    MouseProfile? profile = DecodeHeader(header);
                    if (profile == null || FindLocked(profile.Id) != null)
                    {
                        continue;
                    }

                    _mice.Add(profile);
                }
            }

            if (buttonLines != null)
            {
                foreach (string? line in buttonLines)
                {
                    string text = line ?? "";
                    int bar = text.IndexOf('|');
                    if (bar <= 0)
                    {
                        continue;
                    }

                    MouseProfile? profile = FindLocked(text[..bar]);
                    if (profile != null)
                    {
                        DecodeButtons(profile, text[(bar + 1)..]);
                    }
                }
            }

            if (dayLines != null)
            {
                foreach (string? line in dayLines)
                {
                    string text = line ?? "";
                    int bar = text.IndexOf('|');
                    if (bar <= 0)
                    {
                        continue;
                    }

                    MouseProfile? profile = FindLocked(text[..bar]);
                    if (profile != null)
                    {
                        DecodeDays(profile, text[(bar + 1)..]);
                    }
                }
            }

            if (string.IsNullOrEmpty(_activeId) || FindLocked(_activeId) == null)
            {
                foreach (MouseProfile profile in _mice)
                {
                    if (!profile.Pending)
                    {
                        _activeId = profile.Id;
                        break;
                    }
                }
            }
        }

        _needSeed = false;
        _loaded = true;
    }

    /// <summary>
    /// v3 <c>hardware_v1</c> + <c>wear_mouse_v1</c> 的迁移入口。
    /// 全零计数且没有自定义鼠标名时不生成 legacy 空档案。
    /// </summary>
    public void LoadLegacy(string? hardware, string? wearMouse, bool hasMouse = true)
    {
        BeginLoad();
        if (!hasMouse || wearMouse == null)
        {
            _needSeed = true;
            _loaded = true;
            return;
        }

        string name = LegacyMouseName(hardware);
        if (wearMouse == "0|0|0|0" && (string.IsNullOrEmpty(name) || name == "主力鼠标"))
        {
            _needSeed = !hasMouse || string.IsNullOrEmpty(wearMouse);
            _loaded = true;
            return;
        }

        MouseProfile legacy = CreateLegacyMouse(hardware, wearMouse);
        lock (_gate)
        {
            _mice.Add(legacy);
        }

        _needSeed = false;
        _loaded = true;
    }

    /// <summary>
    /// 首次加载时用历史日记录和全量总数回填旧版单鼠标档案。
    /// 两边都有的计数取较大值，避免历史缺日或 Total 更完整时低估。
    /// </summary>
    public void EnsureSeeded(IEnumerable<DayRecord>? history, StatsTotals? total)
    {
        if (_loaded && !_needSeed)
        {
            return;
        }

        long left = 0;
        long right = 0;
        long middle = 0;
        long x = 0;
        DateTime oldest = DateTime.MinValue;
        if (history != null)
        {
            foreach (DayRecord day in history)
            {
                if (day == null)
                {
                    continue;
                }

                if (oldest == DateTime.MinValue || day.Date < oldest)
                {
                    oldest = day.Date;
                }

                left += day.Left;
                right += day.Right;
                middle += day.Middle;
                x += day.XButtons;
            }
        }

        bool totalHasButtons = total != null
            && (total.Left > 0 || total.Right > 0 || total.Middle > 0 || total.XButtons > 0);
        long seedLeft = totalHasButtons && total!.Left > left ? total.Left : left;
        long seedRight = totalHasButtons && total!.Right > right ? total.Right : right;
        long seedMiddle = totalHasButtons && total!.Middle > middle ? total.Middle : middle;
        long seedX = totalHasButtons && total!.XButtons > x ? total.XButtons : x;

        if (seedLeft + seedRight + seedMiddle + seedX > 0)
        {
            MouseProfile profile = new()
            {
                Id = LegacyId,
                Alias = "旧版单鼠标",
                Legacy = true,
                Seeded = true,
                Since = oldest == DateTime.MinValue ? DateTime.Today : oldest
            };
            profile.Left = seedLeft;
            profile.Right = seedRight;
            profile.Middle = seedMiddle;
            profile.X = seedX;
            lock (_gate)
            {
                _mice.Add(profile);
            }
        }

        _needSeed = false;
        _loaded = true;
    }

    public void Append(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (MouseProfile profile in SnapshotMice(false))
        {
            builder.AppendLine("mouse_v1=" + EncodeHeader(profile));
            builder.AppendLine("mouse_buttons_v1=" + EncodeButtons(profile));
            builder.AppendLine("mouse_days_v1=" + EncodeDays(profile));
        }
    }

    /// <summary>v3 <c>wear_mouse_v1</c>：竖线分隔的旧版按钮计数写入主鼠标。</summary>
    public void LoadButtons(string? text)
    {
        DecodeLegacyButtons(PrimaryMouse, text);
    }

    /// <summary>v3 <c>wear_mouse_v1</c> 的编码（竖线分隔，不带 id）。</summary>
    public string EncodeButtons()
    {
        MouseProfile profile = PrimaryMouse;
        return profile.Left.ToString(CultureInfo.InvariantCulture) + "|"
            + profile.Right.ToString(CultureInfo.InvariantCulture) + "|"
            + profile.Middle.ToString(CultureInfo.InvariantCulture) + "|"
            + profile.X.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>主鼠标近 7 天点击环的编码（不带 id）。</summary>
    public string EncodeDays()
    {
        return EncodeDayPayload(PrimaryMouse);
    }

    // ---------------------------------------------------------------- 编解码

    /// <summary>mouse_v1 头：id|alias|friendly|path|hardware|instance|flags|left|right|since|seeded|lastSeen。</summary>
    public static string EncodeHeader(MouseProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int flags = (profile.BuiltIn ? 1 : 0) | (profile.Legacy ? 2 : 0) | (profile.Pending ? 4 : 0);
        return profile.Id + "|" + PeripheralWear.EncodeName(profile.Alias) + "|"
            + PeripheralWear.EncodeName(profile.FriendlyName) + "|"
            + PeripheralWear.EncodeName(profile.DevicePath) + "|"
            + PeripheralWear.EncodeName(profile.HardwareKey) + "|"
            + PeripheralWear.EncodeName(profile.InstanceKey) + "|"
            + flags.ToString(CultureInfo.InvariantCulture) + "|"
            + profile.RatedLeft.ToString(CultureInfo.InvariantCulture) + "|"
            + profile.RatedRight.ToString(CultureInfo.InvariantCulture) + "|"
            + PeripheralWear.TimeText(profile.Since) + "|"
            + (profile.Seeded ? "1" : "0") + "|"
            + profile.LastSeenUtcTicks.ToString(CultureInfo.InvariantCulture);
    }

    public static MouseProfile? DecodeHeader(string? text)
    {
        string[] parts = (text ?? "").Split('|');
        if (parts.Length < 12 || parts[0].Length == 0)
        {
            return null;
        }

        MouseProfile profile = new()
        {
            Id = parts[0],
            Alias = PeripheralWear.DecodeName(parts[1]),
            FriendlyName = PeripheralWear.DecodeName(parts[2]),
            DevicePath = PeripheralWear.DecodeName(parts[3]),
            HardwareKey = PeripheralWear.DecodeName(parts[4]),
            InstanceKey = PeripheralWear.DecodeName(parts[5])
        };

        int flags = (int)PeripheralWear.ParseLong(parts[6]);
        profile.BuiltIn = (flags & 1) != 0;
        profile.Legacy = (flags & 2) != 0;
        profile.Pending = (flags & 4) != 0;
        profile.RatedLeft = PeripheralWear.ClampRated(
            PeripheralWear.ParseLong(parts[7]), PeripheralWear.DefaultMouseRated);
        profile.RatedRight = PeripheralWear.ClampRated(
            PeripheralWear.ParseLong(parts[8]), PeripheralWear.DefaultMouseRated);
        profile.Since = PeripheralWear.ParseTime(parts[9]);
        profile.Seeded = parts[10] == "1";
        profile.LastSeenUtcTicks = PeripheralWear.ParseLong(parts[11]);
        return profile;
    }

    /// <summary>mouse_buttons_v1：id|left,right,middle,x。</summary>
    public static string EncodeButtons(MouseProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Id + "|" + EncodeButtonPayload(profile);
    }

    public static string EncodeButtonPayload(MouseProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Left.ToString(CultureInfo.InvariantCulture) + ","
            + profile.Right.ToString(CultureInfo.InvariantCulture) + ","
            + profile.Middle.ToString(CultureInfo.InvariantCulture) + ","
            + profile.X.ToString(CultureInfo.InvariantCulture);
    }

    public static void DecodeButtons(MouseProfile profile, string? text)
    {
        ApplyButtons(profile, (text ?? "").Split(','));
    }

    /// <summary>旧版 <c>wear_mouse_v1</c> 使用竖线分隔。</summary>
    public static void DecodeLegacyButtons(MouseProfile profile, string? text)
    {
        ApplyButtons(profile, (text ?? "").Split('|'));
    }

    /// <summary>mouse_days_v1：id|stamp:l,r,m,x;…（无记录写 "-"）。</summary>
    public static string EncodeDays(MouseProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Id + "|" + EncodeDayPayload(profile);
    }

    public static string EncodeDayPayload(MouseProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        StringBuilder builder = new();
        bool any = false;
        for (int i = 0; i < MouseProfile.DaySlots; i++)
        {
            if (profile.DayStamp[i] <= 0)
            {
                continue;
            }

            if (any)
            {
                builder.Append(';');
            }

            builder.Append(profile.DayStamp[i].ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(profile.DayLeft[i].ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(profile.DayRight[i].ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(profile.DayMiddle[i].ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(profile.DayX[i].ToString(CultureInfo.InvariantCulture));
            any = true;
        }

        return any ? builder.ToString() : "-";
    }

    public static void DecodeDays(MouseProfile profile, string? text)
    {
        ArgumentNullException.ThrowIfNull(profile);
        for (int i = 0; i < MouseProfile.DaySlots; i++)
        {
            profile.DayStamp[i] = 0;
            profile.DayLeft[i] = 0;
            profile.DayRight[i] = 0;
            profile.DayMiddle[i] = 0;
            profile.DayX[i] = 0;
        }

        if (string.IsNullOrEmpty(text) || text == "-")
        {
            return;
        }

        string[] parts = text.Split(';');
        int valid = 0;
        foreach (string part in parts)
        {
            int colon = part.IndexOf(':');
            if (colon > 0 && PeripheralWear.ParseLong(part[..colon]) > 0)
            {
                valid++;
            }
        }

        int slot = Math.Max(0, MouseProfile.DaySlots - valid);
        for (int i = 0; i < parts.Length && slot < MouseProfile.DaySlots; i++)
        {
            int colon = parts[i].IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            long stamp = PeripheralWear.ParseLong(parts[i][..colon]);
            if (stamp <= 0)
            {
                continue;
            }

            string[] values = parts[i][(colon + 1)..].Split(',');
            if (values.Length < 4)
            {
                continue;
            }

            profile.DayStamp[slot] = stamp;
            profile.DayLeft[slot] = Math.Max(0, PeripheralWear.ParseLong(values[0]));
            profile.DayRight[slot] = Math.Max(0, PeripheralWear.ParseLong(values[1]));
            profile.DayMiddle[slot] = Math.Max(0, PeripheralWear.ParseLong(values[2]));
            profile.DayX[slot] = Math.Max(0, PeripheralWear.ParseLong(values[3]));
            slot++;
        }
    }

    /// <summary>从 v3 hardware_v1 的鼠标段和 wear_mouse_v1 构造旧版单鼠标档案。</summary>
    public static MouseProfile CreateLegacyMouse(string? hardware, string? wearMouse)
    {
        MouseProfile profile = new()
        {
            Id = LegacyId,
            Legacy = true,
            Seeded = true,
            RatedLeft = PeripheralWear.DefaultMouseRated,
            RatedRight = PeripheralWear.DefaultMouseRated
        };

        string[] fields = (hardware ?? "").Split('|');
        if (fields.Length > 3)
        {
            profile.Alias = PeripheralWear.DecodeName(fields[3]);
        }

        if (fields.Length > 4)
        {
            profile.RatedLeft = PeripheralWear.ClampRated(
                PeripheralWear.ParseLong(fields[4]), PeripheralWear.DefaultMouseRated);
        }

        if (fields.Length > 5)
        {
            profile.RatedRight = PeripheralWear.ClampRated(
                PeripheralWear.ParseLong(fields[5]), PeripheralWear.DefaultMouseRated);
        }

        if (fields.Length > 6)
        {
            profile.Since = PeripheralWear.ParseTime(fields[6]);
        }

        if (string.IsNullOrEmpty(profile.Alias) || profile.Alias == "主力鼠标")
        {
            profile.Alias = "旧版单鼠标";
        }

        DecodeLegacyButtons(profile, wearMouse);
        return profile;
    }

    // ---------------------------------------------------------------- 内部

    private MouseProfile? FindPrimaryLocked()
    {
        MouseProfile? active = FindLocked(_activeId);
        if (active is { Pending: false, Legacy: false } && !IsPlaceholder(active))
        {
            return active;
        }

        foreach (MouseProfile profile in _mice)
        {
            if (!profile.Pending && !profile.Legacy && !IsPlaceholder(profile))
            {
                return profile;
            }
        }

        foreach (MouseProfile profile in _mice)
        {
            if (!profile.Pending && !IsPlaceholder(profile))
            {
                return profile;
            }
        }

        return null;
    }

    private MouseProfile? FindLocked(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        foreach (MouseProfile profile in _mice)
        {
            if (string.Equals(profile.Id, id, StringComparison.Ordinal))
            {
                return profile;
            }
        }

        return null;
    }

    private MouseProfile EnsureDefaultLocked()
    {
        MouseProfile? existing = FindLocked(PrimaryId);
        if (existing != null)
        {
            return existing;
        }

        MouseProfile profile = new()
        {
            Id = PrimaryId,
            Alias = "主力鼠标",
            RatedLeft = PeripheralWear.DefaultMouseRated,
            RatedRight = PeripheralWear.DefaultMouseRated
        };
        _mice.Add(profile);
        return profile;
    }

    private static void ResetProfile(MouseProfile profile, DateTime now)
    {
        profile.Left = 0;
        profile.Right = 0;
        profile.Middle = 0;
        profile.X = 0;
        for (int i = 0; i < MouseProfile.DaySlots; i++)
        {
            profile.DayStamp[i] = 0;
            profile.DayLeft[i] = 0;
            profile.DayRight[i] = 0;
            profile.DayMiddle[i] = 0;
            profile.DayX[i] = 0;
        }

        profile.Since = now;
    }

    private static void AddClicks(MouseProfile from, MouseProfile to)
    {
        to.Left += from.Left;
        to.Right += from.Right;
        to.Middle += from.Middle;
        to.X += from.X;
        from.Left = 0;
        from.Right = 0;
        from.Middle = 0;
        from.X = 0;

        for (int i = 0; i < MouseProfile.DaySlots; i++)
        {
            to.DayLeft[i] += from.DayLeft[i];
            to.DayRight[i] += from.DayRight[i];
            to.DayMiddle[i] += from.DayMiddle[i];
            to.DayX[i] += from.DayX[i];
            if (to.DayStamp[i] == 0)
            {
                to.DayStamp[i] = from.DayStamp[i];
            }
        }
    }

    private static void ApplyButtons(MouseProfile profile, string[] parts)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Left = parts.Length > 0 ? Math.Max(0, PeripheralWear.ParseLong(parts[0])) : 0;
        profile.Right = parts.Length > 1 ? Math.Max(0, PeripheralWear.ParseLong(parts[1])) : 0;
        profile.Middle = parts.Length > 2 ? Math.Max(0, PeripheralWear.ParseLong(parts[2])) : 0;
        profile.X = parts.Length > 3 ? Math.Max(0, PeripheralWear.ParseLong(parts[3])) : 0;
    }

    private static string LegacyMouseName(string? hardware)
    {
        string[] fields = (hardware ?? "").Split('|');
        return fields.Length > 3 ? PeripheralWear.DecodeName(fields[3]) : "";
    }
}
