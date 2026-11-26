namespace Kms.Render.Text;

/// <summary>
/// 字体回退链。顺序即优先级；<see cref="ResolveFamily(string?, IEnumerable{string})"/>
/// 是纯函数，不依赖 Avalonia <c>FontManager</c>，可用任意可用字族集合测试。
/// </summary>
public static class FontChains
{
    public const string CascadiaMono = "Cascadia Mono";
    public const string Consolas = "Consolas";
    public const string CourierNew = "Courier New";
    public const string MicrosoftYaHeiUi = "Microsoft YaHei UI";
    public const string SegoeUi = "Segoe UI";

    private static readonly string[] MonoOrder = [CascadiaMono, Consolas, CourierNew];
    private static readonly string[] UiOrder = [MicrosoftYaHeiUi, SegoeUi];

    /// <summary>等宽键名链：Cascadia Mono → Consolas → Courier New。</summary>
    public static IReadOnlyList<string> Mono { get; } = Array.AsReadOnly(MonoOrder);

    /// <summary>界面/中文链：Microsoft YaHei UI → Segoe UI。</summary>
    public static IReadOnlyList<string> Ui { get; } = Array.AsReadOnly(UiOrder);

    /// <summary>
    /// 从可用字族里解析实际使用的字族。规则：
    /// 1) requested 命中已知链时，从该位置向后找，再回绕到链首；
    /// 2) 未知 requested 存在则原样使用；
    /// 3) 否则走界面链；整条链都缺失时返回链尾作为最后兜底。
    /// </summary>
    /// <param name="requested">调用方想用的字族名，可为空。</param>
    /// <param name="available">系统可用字族名集合，比较不区分大小写。</param>
    public static string ResolveFamily(string? requested, IEnumerable<string> available)
    {
        ArgumentNullException.ThrowIfNull(available);

        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string family in available)
        {
            if (!string.IsNullOrWhiteSpace(family))
            {
                installed.Add(family.Trim());
            }
        }

        return ResolveFamily(requested, installed.Contains);
    }

    /// <summary>
    /// 字体存在性探针版本，便于在不启动 Avalonia 的情况下注入测试。
    /// </summary>
    public static string ResolveFamily(string? requested, Func<string, bool> isAvailable)
    {
        ArgumentNullException.ThrowIfNull(isAvailable);

        if (string.IsNullOrWhiteSpace(requested))
        {
            return FirstAvailable(Ui, isAvailable);
        }

        string name = requested.Trim();
        int monoIndex = IndexOf(Mono, name);
        if (monoIndex >= 0)
        {
            return ResolveFrom(Mono, monoIndex, isAvailable);
        }

        int uiIndex = IndexOf(Ui, name);
        if (uiIndex >= 0)
        {
            return ResolveFrom(Ui, uiIndex, isAvailable);
        }

        return isAvailable(name) ? name : FirstAvailable(Ui, isAvailable);
    }

    private static string ResolveFrom(
        IReadOnlyList<string> chain,
        int start,
        Func<string, bool> isAvailable)
    {
        for (int i = start; i < chain.Count; i++)
        {
            if (isAvailable(chain[i]))
            {
                return chain[i];
            }
        }

        for (int i = 0; i < start; i++)
        {
            if (isAvailable(chain[i]))
            {
                return chain[i];
            }
        }

        return chain[^1];
    }

    private static string FirstAvailable(IReadOnlyList<string> chain, Func<string, bool> isAvailable)
    {
        for (int i = 0; i < chain.Count; i++)
        {
            if (isAvailable(chain[i]))
            {
                return chain[i];
            }
        }

        return chain[^1];
    }

    private static int IndexOf(IReadOnlyList<string> chain, string name)
    {
        for (int i = 0; i < chain.Count; i++)
        {
            if (string.Equals(chain[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
