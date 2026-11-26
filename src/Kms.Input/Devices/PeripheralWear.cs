using System.Globalization;
using System.Text;
using Kms.Core.Model;

namespace Kms.Input.Devices;

/// <summary>Top 键排行的一行。对应 v3 <c>KeyWear</c>。</summary>
public sealed class KeyWear
{
    public int Vk { get; set; }

    public string Name { get; set; } = "";

    public long Count { get; set; }
}

/// <summary>
/// 多键盘硬件纪元计数器。与 Store 的 Total / History 完全独立。
/// <para>
/// 每把键盘拥有独立 Id / Alias / FriendlyName / Rated / Since / <c>long[256]</c>；
/// RawInput 每设备位图去重后累加，低级钩子只负责全局统计。
/// </para>
/// <para>
/// 热路径只做 <see cref="Interlocked"/> 计数与固定列表查找，
/// 不做字符串/注册表/磁盘 I/O。v3 的静态 <c>PeripheralWear</c> 拆成实例，
/// 测试可以各建一份；产品使用 <see cref="Shared"/> 或自行持有实例。
/// </para>
/// </summary>
public sealed class PeripheralWear
{
    public const int KeySlots = KeyboardProfile.KeySlots;

    /// <summary>v3 标定：键盘单键额定 5000 万次（主流薄膜/机械轴寿命量级）。</summary>
    public const long DefaultKeyboardRated = 50_000_000L;

    /// <summary>v3 标定：鼠标单颗微动额定 8000 万次，左右键分别计算。</summary>
    public const long DefaultMouseRated = 80_000_000L;

    /// <summary>额定输入的硬上限，防止异常配置把百分比压成 0。</summary>
    public const long MaxRated = 10_000_000_000L;
    public const string PrimaryId = "primary";
    public const string LegacyId = "legacy";
    public const string UnknownId = "unknown";
    public const string AggregateId = "*";

    private readonly object _gate = new();
    private readonly List<KeyboardProfile> _keyboards = new();
    private string _activeKeyboardId = "";
    private bool _loaded;
    private bool _needKeySeed = true;

    /// <summary>产品默认实例；测试应各自 <c>new PeripheralWear()</c> 以避免共享状态。</summary>
    public static PeripheralWear Shared { get; } = new();

    /// <summary>首次加载后是否还需要从历史记录回填按键计数。</summary>
    public bool NeedsKeySeed
    {
        get
        {
            lock (_gate)
            {
                return !_loaded || _needKeySeed;
            }
        }
    }

    /// <summary>最近解析成功的键盘 Id；用于「当前键盘」视图。</summary>
    public string ActiveKeyboardId
    {
        get
        {
            lock (_gate)
            {
                return _activeKeyboardId;
            }
        }
    }

    /// <summary>主档案：优先当前设备，其次任意非 legacy 档案，最后浮动到 legacy。</summary>
    public KeyboardProfile PrimaryProfile
    {
        get
        {
            lock (_gate)
            {
                return FindPrimaryLocked() ?? EnsureDefaultLocked();
            }
        }
    }

    /// <summary>新装/仅鼠标数据时的空占位：不显示、不落盘，也不阻止真实设备接管。</summary>
    public static bool IsPlaceholder(KeyboardProfile? profile)
    {
        return profile is { Pending: false, Legacy: false, Id: PrimaryId }
            && string.Equals(profile.Alias ?? "", "主力键盘", StringComparison.Ordinal)
            && string.IsNullOrEmpty(profile.FriendlyName)
            && profile.TotalKeyPresses == 0;
    }

    /// <summary>只查找，不创建；用于加载/序列化时避免产生空占位档案。</summary>
    public KeyboardProfile? FindKeyboard(string? id)
    {
        if (string.IsNullOrEmpty(id) || id == AggregateId)
        {
            return null;
        }

        lock (_gate)
        {
            return FindKeyboardLocked(id);
        }
    }

    public List<KeyboardProfile> SnapshotKeyboards(bool includePending = false)
    {
        lock (_gate)
        {
            List<KeyboardProfile> list = new(_keyboards.Count);
            foreach (KeyboardProfile profile in _keyboards)
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

    public void SetActiveProfile(KeyboardProfile? profile)
    {
        if (profile == null)
        {
            return;
        }

        lock (_gate)
        {
            _activeKeyboardId = profile.Id;
        }

        profile.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>主档案显示名（别名优先）。设置后清除 pending 并标记已初始化。</summary>
    public string KeyboardName
    {
        get => PrimaryProfile.DisplayName;
        set
        {
            KeyboardProfile profile = PrimaryProfile;
            profile.Alias = value ?? "";
            profile.Pending = false;
            profile.Seeded = true;
        }
    }

    public long KeyboardRated
    {
        get => PrimaryProfile.Rated;
        set => PrimaryProfile.Rated = ClampRated(value, DefaultKeyboardRated);
    }

    public DateTime KeyboardSince
    {
        get => PrimaryProfile.Since;
        set => PrimaryProfile.Since = value;
    }

    public bool Seeded
    {
        get => PrimaryProfile.Seeded;
        set => PrimaryProfile.Seeded = value;
    }

    // ---------------------------------------------------------------- 计数

    /// <summary>兼容旧 API：计入主档案。</summary>
    public void CountKey(int vk)
    {
        CountKey(PrimaryProfile, vk);
    }

    public void CountKey(KeyboardProfile? profile, int vk)
    {
        profile ??= UnknownProfile;
        if (vk < 0 || vk >= KeySlots)
        {
            return;
        }

        Interlocked.Increment(ref profile.Keys[vk]);
    }

    public void CountUnknown(int vk)
    {
        CountKey(UnknownProfile, vk);
    }

    /// <summary>读取主档案的某个键。热路径请直接用 <see cref="Interlocked.Read"/>。</summary>
    public long ReadKey(int vk)
    {
        return ReadKey(PrimaryProfile, vk);
    }

    public long ReadKey(KeyboardProfile? profile, int vk)
    {
        if (profile == null || vk < 0 || vk >= KeySlots)
        {
            return 0;
        }

        return Interlocked.Read(ref profile.Keys[vk]);
    }

    public long TotalKeyPresses => PrimaryProfile.TotalKeyPresses;

    public long TotalKeyPressesAll
    {
        get
        {
            long total = 0;
            foreach (KeyboardProfile profile in SnapshotKeyboards(false))
            {
                total += profile.TotalKeyPresses;
            }

            return total;
        }
    }

    // ---------------------------------------------------------------- 档案

    public KeyboardProfile EnsureDefault()
    {
        lock (_gate)
        {
            if (_keyboards.Count == 0)
            {
                return EnsureDefaultLocked();
            }

            return FindPrimaryLocked() ?? EnsureDefaultLocked();
        }
    }

    /// <summary>RawInput 不可用/解析失败时的回退档案。</summary>
    public KeyboardProfile UnknownProfile
    {
        get
        {
            lock (_gate)
            {
                KeyboardProfile? profile = FindKeyboardLocked(UnknownId);
                if (profile != null)
                {
                    return profile;
                }

                profile = new KeyboardProfile
                {
                    Id = UnknownId,
                    Alias = "未知键盘",
                    FriendlyName = "RawInput 不可用时的回退档案",
                    Seeded = true
                };
                _keyboards.Add(profile);
                return profile;
            }
        }
    }

    /// <summary>RawInput 首次出现：先建 pending 档案，后台解析后再归并。</summary>
    public KeyboardProfile CreatePendingProfile(IntPtr handle)
    {
        KeyboardProfile profile = new()
        {
            Id = "pending:" + handle.ToInt64().ToString("X", CultureInfo.InvariantCulture),
            Alias = "识别中…",
            Pending = true,
            Seeded = true,
            Since = DateTime.Today
        };
        lock (_gate)
        {
            _keyboards.Add(profile);
        }

        return profile;
    }

    /// <summary>
    /// 把 pending 档案并入稳定身份。相同 InstanceKey 优先，其次是唯一的 HardwareKey；
    /// 都找不到时 pending 原地升级。identity 为 <c>null</c> 时落到「未知键盘」。
    /// </summary>
    public KeyboardProfile? ResolvePending(KeyboardProfile? pending, KeyboardIdentity? identity)
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

            KeyboardProfile? blank = FindKeyboardLocked(PrimaryId);
            if (blank != null && blank != pending && IsPlaceholder(blank))
            {
                _keyboards.Remove(blank);
            }

            if (identity == null)
            {
                // 已存在 unknown 档案时并入，避免出现两个同 Id 档案。
                KeyboardProfile? unknown = FindKeyboardLocked(UnknownId);
                if (unknown != null && unknown != pending)
                {
                    MoveKeys(pending, unknown);
                    _keyboards.Remove(pending);
                    return unknown;
                }

                pending.Id = UnknownId;
                pending.Alias = "未知键盘";
                pending.FriendlyName = "";
                pending.Pending = false;
                _activeKeyboardId = pending.Id;
                return pending;
            }

            KeyboardProfile? target = null;
            foreach (KeyboardProfile profile in _keyboards)
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
                KeyboardProfile? only = null;
                foreach (KeyboardProfile profile in _keyboards)
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
                // 极端情况下旧档案丢了 Instance/Hardware（手改文件、旧版本），
                // 但 Id 仍相同；此时并入而不是造出两个同 Id 档案。
                KeyboardProfile? sameId = FindKeyboardLocked(identity.Id);
                if (sameId != null && sameId != pending)
                {
                    target = sameId;
                    MoveKeys(pending, target);
                    _keyboards.Remove(pending);
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
                MoveKeys(pending, target);
                _keyboards.Remove(pending);
            }

            target.FriendlyName = identity.FriendlyName ?? "";
            target.DevicePath = identity.DevicePath ?? "";
            target.HardwareKey = identity.HardwareKey ?? "";
            target.InstanceKey = identity.InstanceKey ?? "";
            target.BuiltIn = identity.BuiltIn;
            target.Legacy = false;
            target.Seeded = true;
            target.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
            _activeKeyboardId = target.Id;
            return target;
        }
    }

    /// <summary>把旧版单键盘的全部计数并入目标键盘。目标不存在或没有 legacy 时返回 false。</summary>
    public bool MergeLegacyInto(string targetId)
    {
        lock (_gate)
        {
            KeyboardProfile? legacy = FindKeyboardLocked(LegacyId);
            KeyboardProfile? target = FindKeyboardLocked(targetId);
            if (legacy == null || target == null || legacy == target)
            {
                return false;
            }

            MoveKeys(legacy, target);
            if (target.Since == DateTime.MinValue)
            {
                target.Since = legacy.Since;
            }

            _keyboards.Remove(legacy);
            return true;
        }
    }

    // ---------------------------------------------------------------- 重置

    public void ResetKeyboard(DateTime now)
    {
        PrimaryProfile.Reset(now);
    }

    public void ResetKeyboard(string id, DateTime now)
    {
        FindKeyboard(id)?.Reset(now);
    }

    // ---------------------------------------------------------------- TOP / 状态

    public List<KeyWear> TopKeys(int count)
    {
        return TopKeys("", count);
    }

    /// <summary>
    /// 按显示名合并（左右 Shift/Ctrl/Alt 归为同一个键），降序取前 N。
    /// profileId 为空或 <see cref="AggregateId"/> 时汇总全部键盘。
    /// </summary>
    public List<KeyWear> TopKeys(string? profileId, int count)
    {
        long[] counts = new long[KeySlots];
        if (string.IsNullOrEmpty(profileId) || profileId == AggregateId)
        {
            foreach (KeyboardProfile profile in SnapshotKeyboards(false))
            {
                for (int vk = 0; vk < KeySlots; vk++)
                {
                    counts[vk] += Interlocked.Read(ref profile.Keys[vk]);
                }
            }
        }
        else
        {
            KeyboardProfile? profile = FindKeyboard(profileId);
            if (profile != null)
            {
                for (int vk = 0; vk < KeySlots; vk++)
                {
                    counts[vk] = Interlocked.Read(ref profile.Keys[vk]);
                }
            }
        }

        Dictionary<string, KeyWear> grouped = new(StringComparer.Ordinal);
        for (int vk = 0; vk < KeySlots; vk++)
        {
            long value = counts[vk];
            if (value <= 0)
            {
                continue;
            }

            string name = KeyName(vk);
            if (string.IsNullOrEmpty(name))
            {
                name = "`";
            }

            if (!grouped.TryGetValue(name, out KeyWear? row))
            {
                row = new KeyWear { Vk = vk, Name = name };
                grouped[name] = row;
            }

            row.Count += value;
        }

        List<KeyWear> result = new(grouped.Values);
        result.Sort((a, b) =>
        {
            int order = b.Count.CompareTo(a.Count);
            return order != 0 ? order : string.CompareOrdinal(a.Name, b.Name);
        });

        if (count < 0)
        {
            count = 0;
        }

        if (result.Count > count)
        {
            result.RemoveRange(count, result.Count - count);
        }

        return result;
    }

    /// <summary>0..1 的进度，超过额定按 100% 夹紧。</summary>
    public static double Progress(long count, long rated)
    {
        double ratio = Ratio(count, rated);
        if (ratio < 0)
        {
            return 0;
        }

        return ratio > 1 ? 1 : ratio;
    }

    /// <summary>真实磨损比例，可超过 1；额定未知（&lt;=0）但已有计数时按 100%。</summary>
    public static double Ratio(long count, long rated)
    {
        if (count <= 0)
        {
            return 0;
        }

        if (rated <= 0)
        {
            return 1;
        }

        return count / (double)rated;
    }

    /// <summary>键盘侧的最坏单键比例；鼠标侧由 <see cref="PeripheralMouseWear.WorstRatio"/> 提供。</summary>
    public double WorstRatio()
    {
        double worst = 0;
        foreach (KeyboardProfile profile in SnapshotKeyboards(false))
        {
            for (int vk = 0; vk < KeySlots; vk++)
            {
                long value = Interlocked.Read(ref profile.Keys[vk]);
                if (value <= 0)
                {
                    continue;
                }

                double ratio = Ratio(value, profile.Rated);
                if (ratio > worst)
                {
                    worst = ratio;
                }
            }
        }

        return worst;
    }

    // ---------------------------------------------------------------- 加载 / 落盘

    /// <summary>清空全部档案；加载前调用，避免与旧设备串味。</summary>
    public void BeginLoad()
    {
        lock (_gate)
        {
            _keyboards.Clear();
            _activeKeyboardId = "";
        }

        _needKeySeed = true;
        _loaded = false;
    }

    /// <summary>测试用：清空并留下默认占位，恢复真实 resolver。</summary>
    public void ResetForTest()
    {
        BeginLoad();
        lock (_gate)
        {
            if (_keyboards.Count == 0)
            {
                EnsureDefaultLocked();
            }
        }
    }

    /// <summary>从已解码的档案列表装载；列表中的对象会被直接引用，实时计数可见。</summary>
    public void LoadProfiles(IEnumerable<KeyboardProfile>? profiles)
    {
        lock (_gate)
        {
            _keyboards.Clear();
            if (profiles != null)
            {
                foreach (KeyboardProfile profile in profiles)
                {
                    if (string.IsNullOrEmpty(profile.Id) || FindKeyboardLocked(profile.Id) != null)
                    {
                        continue;
                    }

                    _keyboards.Add(profile);
                }
            }

            if (_keyboards.Count == 0)
            {
                EnsureDefaultLocked();
            }

            // 没有显式 active 时选第一把非 pending 键盘。
            if (string.IsNullOrEmpty(_activeKeyboardId) || FindKeyboardLocked(_activeKeyboardId) == null)
            {
                foreach (KeyboardProfile profile in _keyboards)
                {
                    if (!profile.Pending)
                    {
                        _activeKeyboardId = profile.Id;
                        break;
                    }
                }
            }
        }

        _needKeySeed = false;
        _loaded = true;
    }

    /// <summary>从 <c>keyboard_v1</c> / <c>keyboard_keys_v1</c> 文本行装载。</summary>
    public void LoadProfiles(IEnumerable<string>? headers, IEnumerable<string>? keyLines)
    {
        lock (_gate)
        {
            _keyboards.Clear();
            if (headers != null)
            {
                foreach (string header in headers)
                {
                    KeyboardProfile? profile = DecodeHeader(header);
                    if (profile == null || FindKeyboardLocked(profile.Id) != null)
                    {
                        continue;
                    }

                    _keyboards.Add(profile);
                }
            }

            if (_keyboards.Count == 0)
            {
                EnsureDefaultLocked();
            }

            if (keyLines != null)
            {
                foreach (string? line in keyLines)
                {
                    string text = line ?? "";
                    int bar = text.IndexOf('|');
                    if (bar <= 0)
                    {
                        continue;
                    }

                    KeyboardProfile? profile = FindKeyboardLocked(text[..bar]);
                    if (profile != null)
                    {
                        DecodeKeys(profile, text[(bar + 1)..]);
                    }
                }
            }

            if (string.IsNullOrEmpty(_activeKeyboardId) || FindKeyboardLocked(_activeKeyboardId) == null)
            {
                foreach (KeyboardProfile profile in _keyboards)
                {
                    if (!profile.Pending)
                    {
                        _activeKeyboardId = profile.Id;
                        break;
                    }
                }
            }
        }

        _needKeySeed = false;
        _loaded = true;
    }

    /// <summary>
    /// v3 <c>hardware_v1</c> + <c>wear_keys_v1</c> 的迁移入口：
    /// 只有真实键数（非空且非 "-"）才生成 legacy 档案，空数据不产生幽灵档案。
    /// </summary>
    public void LoadLegacy(string? hardware, string? wearKeys, bool hasKeys = true)
    {
        BeginLoad();
        if (!hasKeys || string.IsNullOrEmpty(wearKeys) || wearKeys == "-")
        {
            // 用户明确重置过（hasKeys 且 "-"）时绝不能再从历史回填。
            _needKeySeed = !hasKeys;
            _loaded = true;
            return;
        }

        KeyboardProfile legacy = CreateLegacyKeyboard(hardware, wearKeys);
        lock (_gate)
        {
            _keyboards.Add(legacy);
        }

        _needKeySeed = false;
        _loaded = true;
    }

    /// <summary>
    /// 首次加载时用历史日记录回填主档案：有 KeyCounts 用 KeyCounts，
    /// 只有 PhysicalVks 的旧记录也能估算。真实历史会升级为可归并的 legacy。
    /// </summary>
    public void EnsureSeeded(IEnumerable<DayRecord>? history, StatsTotals? total)
    {
        _ = total; // 键盘口径只取逐键历史，总计数由鼠标/UI 使用。
        if (_loaded && !_needKeySeed)
        {
            return;
        }

        long[] keySeed = new long[KeySlots];
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

                if (day.KeyCounts.Count > 0)
                {
                    foreach ((int vk, long value) in day.KeyCounts)
                    {
                        if (vk >= 0 && vk < KeySlots && value > 0)
                        {
                            keySeed[vk] += value;
                        }
                    }
                }
                else
                {
                    foreach ((int vk, long value) in day.PhysicalVks)
                    {
                        if (vk >= 0 && vk < KeySlots && value > 0)
                        {
                            keySeed[vk] += value;
                        }
                    }
                }
            }
        }

        DateTime since = oldest == DateTime.MinValue ? DateTime.Today : oldest;
        KeyboardProfile? seedTarget = null;
        if (_needKeySeed)
        {
            lock (_gate)
            {
                seedTarget = FindKeyboardLocked(PrimaryId);
                if (seedTarget == null && _keyboards.Count == 0)
                {
                    seedTarget = EnsureDefaultLocked();
                }
            }

            if (seedTarget != null)
            {
                long seeded = 0;
                for (int vk = 0; vk < KeySlots; vk++)
                {
                    if (keySeed[vk] <= 0)
                    {
                        continue;
                    }

                    Interlocked.Exchange(ref seedTarget.Keys[vk], keySeed[vk]);
                    seeded += keySeed[vk];
                }

                if (seeded > 0)
                {
                    if (seedTarget.Since == DateTime.MinValue)
                    {
                        seedTarget.Since = since;
                    }

                    seedTarget.Seeded = true;
                    // 只有真实历史计数才升级为 legacy，空占位继续隐藏。
                    if (seedTarget.Id == PrimaryId)
                    {
                        seedTarget.Id = LegacyId;
                        seedTarget.Legacy = true;
                        if (string.IsNullOrEmpty(seedTarget.Alias) || seedTarget.Alias == "主力键盘")
                        {
                            seedTarget.Alias = "旧版单键盘";
                        }
                    }
                }
            }
        }

        if (seedTarget != null && seedTarget.Since == DateTime.MinValue)
        {
            seedTarget.Since = since;
        }

        _needKeySeed = false;
        _loaded = true;
    }

    public void AppendProfiles(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (KeyboardProfile profile in SnapshotKeyboards(false))
        {
            builder.AppendLine("keyboard_v1=" + EncodeHeader(profile));
            builder.AppendLine("keyboard_keys_v1=" + EncodeKeys(profile));
        }
    }

    /// <summary>v3 <c>hardware_v1</c> 键盘段：只更新主档案的名称/额定/起始/Seeded。</summary>
    public void LoadProfile(string? hardware)
    {
        string[] parts = (hardware ?? "").Split('|');
        if (parts.Length < 8)
        {
            return;
        }

        KeyboardProfile profile = PrimaryProfile;
        string name = DecodeName(parts[0]);
        if (name.Length > 0)
        {
            profile.Alias = name;
        }

        profile.Rated = ClampRated(ParseLong(parts[1]), DefaultKeyboardRated);
        profile.Since = ParseTime(parts[2]);
        profile.Seeded = parts[7] == "1";
    }

    /// <summary>v3 <c>wear_keys_v1</c>：把旧版单键盘计数写入主档案。</summary>
    public void LoadKeys(string? text)
    {
        DecodeKeys(PrimaryProfile, text);
    }

    /// <summary>v3 <c>wear_keys_v1</c> 的编码（不带 id 前缀）。</summary>
    public string EncodeKeys()
    {
        return EncodeKeyPayload(PrimaryProfile);
    }

    // ---------------------------------------------------------------- 编解码

    /// <summary>keyboard_v1 头：id|alias|friendly|path|hardware|instance|flags|rated|since|seeded|lastSeen。</summary>
    public static string EncodeHeader(KeyboardProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int flags = (profile.BuiltIn ? 1 : 0) | (profile.Legacy ? 2 : 0) | (profile.Pending ? 4 : 0);
        return profile.Id + "|" + EncodeName(profile.Alias) + "|"
            + EncodeName(profile.FriendlyName) + "|"
            + EncodeName(profile.DevicePath) + "|"
            + EncodeName(profile.HardwareKey) + "|"
            + EncodeName(profile.InstanceKey) + "|"
            + flags.ToString(CultureInfo.InvariantCulture) + "|"
            + profile.Rated.ToString(CultureInfo.InvariantCulture) + "|"
            + TimeText(profile.Since) + "|"
            + (profile.Seeded ? "1" : "0") + "|"
            + profile.LastSeenUtcTicks.ToString(CultureInfo.InvariantCulture);
    }

    public static KeyboardProfile? DecodeHeader(string? text)
    {
        string[] parts = (text ?? "").Split('|');
        if (parts.Length < 11 || parts[0].Length == 0)
        {
            return null;
        }

        KeyboardProfile profile = new()
        {
            Id = parts[0],
            Alias = DecodeName(parts[1]),
            FriendlyName = DecodeName(parts[2]),
            DevicePath = DecodeName(parts[3]),
            HardwareKey = DecodeName(parts[4]),
            InstanceKey = DecodeName(parts[5])
        };

        int flags = (int)ParseLong(parts[6]);
        profile.BuiltIn = (flags & 1) != 0;
        profile.Legacy = (flags & 2) != 0;
        profile.Pending = (flags & 4) != 0;
        profile.Rated = ClampRated(ParseLong(parts[7]), DefaultKeyboardRated);
        profile.Since = ParseTime(parts[8]);
        profile.Seeded = parts[9] == "1";
        profile.LastSeenUtcTicks = ParseLong(parts[10]);
        return profile;
    }

    /// <summary>keyboard_keys_v1：id|vk:count,…（全零写 "-"）。</summary>
    public static string EncodeKeys(KeyboardProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Id + "|" + EncodeKeyPayload(profile);
    }

    public static string EncodeKeyPayload(KeyboardProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        StringBuilder builder = new();
        for (int vk = 0; vk < KeySlots; vk++)
        {
            long value = Interlocked.Read(ref profile.Keys[vk]);
            if (value <= 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            builder.Append(vk.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(value.ToString(CultureInfo.InvariantCulture));
        }

        return builder.Length == 0 ? "-" : builder.ToString();
    }

    public static void DecodeKeys(KeyboardProfile profile, string? text)
    {
        ArgumentNullException.ThrowIfNull(profile);
        for (int i = 0; i < KeySlots; i++)
        {
            Interlocked.Exchange(ref profile.Keys[i], 0);
        }

        if (string.IsNullOrEmpty(text) || text == "-")
        {
            return;
        }

        foreach (string entry in text.Split(','))
        {
            int colon = entry.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            int vk = (int)ParseLong(entry[..colon]);
            long value = ParseLong(entry[(colon + 1)..]);
            if (vk >= 0 && vk < KeySlots && value > 0)
            {
                Interlocked.Exchange(ref profile.Keys[vk], value);
            }
        }
    }

    /// <summary>从 v3 hardware_v1 的键盘段和 wear_keys_v1 构造旧版单键盘档案。</summary>
    public static KeyboardProfile CreateLegacyKeyboard(string? hardware, string? wearKeys)
    {
        KeyboardProfile profile = new()
        {
            Id = LegacyId,
            Legacy = true,
            Rated = DefaultKeyboardRated
        };

        string[] fields = (hardware ?? "").Split('|');
        if (!string.IsNullOrEmpty(hardware))
        {
            profile.Alias = DecodeName(fields[0]);
            if (fields.Length > 1)
            {
                profile.Rated = ClampRated(ParseLong(fields[1]), DefaultKeyboardRated);
            }

            if (fields.Length > 2)
            {
                profile.Since = ParseTime(fields[2]);
            }

            if (fields.Length > 7)
            {
                profile.Seeded = fields[7] == "1";
            }
        }

        if (string.IsNullOrEmpty(profile.Alias) || profile.Alias == "主力键盘")
        {
            profile.Alias = "旧版单键盘";
        }

        DecodeKeys(profile, wearKeys);
        return profile;
    }

    /// <summary>键位显示名。与 v3 Dashboard/Analysis 的映射保持一致。</summary>
    public static string KeyName(int vk)
    {
        switch (vk)
        {
            case 0x08: return "退格";
            case 0x09: return "Tab";
            case 0x0D: return "回车";
            case 0x13: return "Pause";
            case 0x14: return "大写锁定";
            case 0x1B: return "Esc";
            case 0x20: return "空格";
            case 0x21: return "PgUp";
            case 0x22: return "PgDn";
            case 0x23: return "End";
            case 0x24: return "Home";
            case 0x25: return "←";
            case 0x26: return "↑";
            case 0x27: return "→";
            case 0x28: return "↓";
            case 0x2C: return "PrtSc";
            case 0x2D: return "Insert";
            case 0x2E: return "Delete";
            case 0x5B: return "Win";
            case 0x5C: return "Win";
            case 0x5D: return "菜单键";
            case 0x6A: return "小键盘*";
            case 0x6B: return "小键盘+";
            case 0x6D: return "小键盘-";
            case 0x6E: return "小键盘.";
            case 0x6F: return "小键盘/";
            case 0x90: return "NumLock";
            case 0x91: return "ScrollLock";
            case 0xA0:
            case 0xA1:
            case 0x10:
                return "Shift";
            case 0xA2:
            case 0xA3:
            case 0x11:
                return "Ctrl";
            case 0xA4:
            case 0xA5:
            case 0x12:
                return "Alt";
            case 0xBA: return ";";
            case 0xBB: return "=";
            case 0xBC: return ",";
            case 0xBD: return "-";
            case 0xBE: return ".";
            case 0xBF: return "/";
            case 0xC0: return "";
            case 0xDB: return "[";
            case 0xDC: return "\\";
            case 0xDD: return "]";
            case 0xDE: return "'";
            default:
                break;
        }

        if (vk is >= 0x30 and <= 0x39)
        {
            return ((char)('0' + vk - 0x30)).ToString();
        }

        if (vk is >= 0x41 and <= 0x5A)
        {
            return ((char)('A' + vk - 0x41)).ToString();
        }

        if (vk is >= 0x60 and <= 0x69)
        {
            return "小键盘" + (vk - 0x60).ToString(CultureInfo.InvariantCulture);
        }

        if (vk is >= 0x70 and <= 0x87)
        {
            return "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture);
        }

        return "0x" + vk.ToString("X2", CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- 基础解析

    public static long ClampRated(long value, long fallback)
    {
        if (value <= 0)
        {
            return fallback;
        }

        return value > MaxRated ? MaxRated : value;
    }

    public static long ParseLong(string? text)
    {
        long value;
        long.TryParse((text ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        return value;
    }

    public static string TimeText(DateTime value)
    {
        return value == DateTime.MinValue ? "0" : value.Ticks.ToString(CultureInfo.InvariantCulture);
    }

    public static DateTime ParseTime(string? text)
    {
        long ticks = ParseLong(text);
        if (ticks <= 0)
        {
            return DateTime.MinValue;
        }

        try
        {
            return new DateTime(ticks);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>v3 的 Base64(UTF-8) 编码；空串保持空串。</summary>
    public static string EncodeName(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>宽松解码：非法 Base64 原样返回，和 v3 一致。</summary>
    public static string DecodeName(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(text));
        }
        catch (FormatException)
        {
            return text;
        }
    }

    // ---------------------------------------------------------------- 内部

    private KeyboardProfile? FindPrimaryLocked()
    {
        KeyboardProfile? active = FindKeyboardLocked(_activeKeyboardId);
        if (active is { Pending: false, Legacy: false })
        {
            return active;
        }

        foreach (KeyboardProfile profile in _keyboards)
        {
            if (!profile.Pending && !profile.Legacy)
            {
                return profile;
            }
        }

        foreach (KeyboardProfile profile in _keyboards)
        {
            if (!profile.Pending)
            {
                return profile;
            }
        }

        return null;
    }

    private KeyboardProfile? FindKeyboardLocked(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        foreach (KeyboardProfile profile in _keyboards)
        {
            if (string.Equals(profile.Id, id, StringComparison.Ordinal))
            {
                return profile;
            }
        }

        return null;
    }

    private KeyboardProfile EnsureDefaultLocked()
    {
        KeyboardProfile? existing = FindKeyboardLocked(PrimaryId);
        if (existing != null)
        {
            return existing;
        }

        KeyboardProfile profile = new()
        {
            Id = PrimaryId,
            Alias = "主力键盘",
            Rated = DefaultKeyboardRated,
            Since = DateTime.MinValue
        };
        _keyboards.Add(profile);
        return profile;
    }

    private static void MoveKeys(KeyboardProfile from, KeyboardProfile to)
    {
        for (int vk = 0; vk < KeySlots; vk++)
        {
            long add = Interlocked.Exchange(ref from.Keys[vk], 0);
            if (add > 0)
            {
                Interlocked.Add(ref to.Keys[vk], add);
            }
        }
    }
}
