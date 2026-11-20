using System.Drawing;
using System.Drawing.Drawing2D;

namespace KeyMouseStats
{
    /// <summary>
    /// 无状态绘制原语。主题层与页面层都可以引用,避免主题反向依赖 Dashboard。
    /// </summary>
    internal static class UiPrimitives
    {
        internal static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            GraphicsPath p = new GraphicsPath();
            if (w < r * 2) r = w / 2;
            if (h < r * 2) r = h / 2;
            if (r < 0.5f) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
            p.AddArc(x, y, r * 2, r * 2, 180, 90);
            p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
