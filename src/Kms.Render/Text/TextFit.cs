using System.Globalization;

namespace Kms.Render.Text;

/// <summary>
/// 纯函数文本适配。省略号用二分查找，不做 v3 <c>TextFit.Ellipsis</c> 的逐字测量。
/// 前缀边界按 Unicode 文本元素（grapheme cluster）切分，不会拆开代理对或组合字符。
/// </summary>
public static class TextFit
{
    public const string Ellipsis = "…";

    /// <summary>文本在给定宽度下是否放得下；空串恒为 true。</summary>
    public static bool Fits(string text, double maxWidth, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);

        return text.Length == 0 || measure(text) <= NormalizeWidth(maxWidth);
    }

    /// <summary>
    /// 超宽时返回「前缀 + …」，否则原样返回。语义对齐 v3：
    /// 放得下返回全文；省略号本身放不下返回空串；一个前缀都放不下返回「…」。
    /// </summary>
    /// <param name="text">待裁剪文本。</param>
    /// <param name="maxWidth">可用宽度（DIP）；NaN 视为不约束，负数按 0 处理。</param>
    /// <param name="measure">
    /// 注入的宽度测量委托；对同一文本应返回稳定值，且前缀宽度随文本增长单调不减。
    /// </param>
    public static string EllipsisByWidth(string text, double maxWidth, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);

        if (text.Length == 0)
        {
            return string.Empty;
        }

        double width = NormalizeWidth(maxWidth);
        if (measure(text) <= width)
        {
            return text;
        }

        double tailWidth = measure(Ellipsis);
        if (tailWidth >= width)
        {
            return string.Empty;
        }

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        double budget = width - tailWidth;
        int low = 1;
        int high = starts.Length - 1;
        int best = 0;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            string head = text[..starts[mid]];

            if (measure(head) <= budget)
            {
                best = starts[mid];
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best > 0 ? text[..best] + Ellipsis : Ellipsis;
    }

    /// <summary>
    /// 在给定宽度下最多能放下多少个 UTF-16 码元（v3 <c>VisibleChars</c> 的替代）。
    /// 返回值总是文本元素边界，因此不会落到代理对中间。
    /// </summary>
    public static int VisibleCharsByWidth(string text, double maxWidth, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);

        double width = NormalizeWidth(maxWidth);
        if (text.Length == 0 || width <= 0)
        {
            return 0;
        }

        if (measure(text) <= width)
        {
            return text.Length;
        }

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int low = 1;
        int high = starts.Length - 1;
        int best = 0;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            string head = text[..starts[mid]];

            if (measure(head) <= width)
            {
                best = starts[mid];
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }

    private static double NormalizeWidth(double maxWidth) =>
        double.IsNaN(maxWidth) ? double.PositiveInfinity : maxWidth < 0 ? 0 : maxWidth;
}
