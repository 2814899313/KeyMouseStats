using Kms.Core.IO;

namespace Kms.Core.Model;

/// <summary>单个应用 / 窗口标题的日聚合。对应 v3 <c>AppUsage</c>。</summary>
public sealed class AppUsage
{
    public string ProcessPath { get; set; } = "";
    public string Title { get; set; } = "";
    public long Keys { get; set; }
    public long Clicks { get; set; }
    public long Wheel { get; set; }
    public double ActiveSeconds { get; set; }

    /// <summary>六类键位分组计数；<see cref="HasKeyGroups"/> 区分「全为零」与「旧记录没有该维度」。</summary>
    public long[] KeyGroups { get; } = new long[6];

    public bool HasKeyGroups { get; set; }

    /// <summary>v3 主键：<c>ProcessPath.ToLowerInvariant() + "\n" + Title</c>。</summary>
    public string Id => ProcessPath.ToLowerInvariant() + "\n" + Title;

    public string AppName => ProcessPath.Length == 0 ? "未识别应用" : PathNames.FileName(ProcessPath);

    public AppUsage Copy()
    {
        AppUsage copy = new()
        {
            ProcessPath = ProcessPath,
            Title = Title,
            Keys = Keys,
            Clicks = Clicks,
            Wheel = Wheel,
            ActiveSeconds = ActiveSeconds,
            HasKeyGroups = HasKeyGroups
        };
        Array.Copy(KeyGroups, copy.KeyGroups, KeyGroups.Length);
        return copy;
    }
}
