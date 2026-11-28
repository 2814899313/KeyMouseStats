using Kms.App.Layout;

namespace Kms.Golden.Tests;

/// <summary>测试使用空布局目录：永不读取用户真实的 layouts.json。</summary>
internal static class GoldenLayouts
{
    public static LayoutConfigService Empty { get; } = new(
        Path.Combine(Path.GetTempPath(), "kms-golden-layouts-" + Guid.NewGuid().ToString("N")));
}
