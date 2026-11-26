using Avalonia.Media;

namespace Kms.Render.Text;

/// <summary>
/// 一次文本度量的结果：尺寸 + 可复用的 <see cref="FormattedText"/>。
/// 注入式度量（测试/无渲染后端）没有 <see cref="FormattedText"/>，此时为 null。
/// </summary>
public sealed class TextMeasureEntry
{
    public TextMeasureEntry(TextMetrics metrics, FormattedText? formattedText = null)
    {
        Metrics = metrics;
        FormattedText = formattedText;
    }

    public TextMetrics Metrics { get; }

    /// <summary>缓存的排版对象。共享实例，调用方不得修改其文本/字号/宽度。</summary>
    public FormattedText? FormattedText { get; }

    public double Width => Metrics.Width;

    public double Height => Metrics.Height;

    public double Baseline => Metrics.Baseline;
}
