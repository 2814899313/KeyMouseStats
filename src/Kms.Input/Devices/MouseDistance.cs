namespace Kms.Input.Devices;

/// <summary>Raw Input 传感器计数 → 物理距离（v3 <c>MouseDistance</c>）。</summary>
public static class MouseDistance
{
    public static bool ValidDpi(double dpi) =>
        !double.IsNaN(dpi) && dpi >= 1 && dpi <= 1_000_000;

    /// <summary>counts 为 Raw Input 的相对位移长度；DPI 无效时返回 0。</summary>
    public static double ToMeters(double counts, double dpi) =>
        ValidDpi(dpi) ? counts / dpi * 0.0254 : 0;
}
