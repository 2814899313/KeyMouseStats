namespace Kms.Core.Privacy;

/// <summary>
/// 导出 / 固件清洗用的脱敏纯函数（移植自 v3 <c>PrivacyMask.cs</c>）。
/// 全部幂等，且不读写文件、不依赖 UI。是否启用在调用方决定。
/// </summary>
public static class PrivacyMask
{
    public const string HiddenSuffix = " · 已隐藏";

    /// <summary>全角星号，用于保长替换。</summary>
    public const char MaskChar = '\uFF0A';

    private static readonly string[] CategoryNames =
    [
        "编程", "游戏", "视频", "其他", "未分类", "办公", "科研", "写作", "沟通", "娱乐"
    ];

    public static string CategoryName(int categoryId) =>
        categoryId < 0 || categoryId >= CategoryNames.Length
            ? "其他"
            : CategoryNames[categoryId];

    /// <summary>窗口标题脱敏：替换为「语义类别 · 已隐藏」。</summary>
    public static string MaskTitle(string? title, int categoryId) =>
        CategoryName(categoryId) + HiddenSuffix;

    /// <summary>文本脱敏：保留长度与空白结构，其余字符替换为 ＊。</summary>
    public static string MaskText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                span[i] = char.IsWhiteSpace(c) ? c : MaskChar;
            }
        });
    }

    /// <summary>
    /// 路径脱敏：只保留文件名。兼容 Windows 与 Unix 分隔符，
    /// 因为 Kms.Core 的测试要在 Linux 上跑。
    /// </summary>
    public static string MaskPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        int cut = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        string name = cut >= 0 ? path[(cut + 1)..] : path;
        return string.IsNullOrEmpty(name) ? "已隐藏" : name;
    }

    public static IReadOnlyList<string> MaskTitles(IEnumerable<string>? titles, int categoryId)
    {
        if (titles is null)
        {
            return [];
        }

        List<string> result = [];
        foreach (string title in titles)
        {
            result.Add(MaskTitle(title, categoryId));
        }

        return result;
    }
}
