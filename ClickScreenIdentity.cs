// ============================================================================
//  KeyMouseStats v3.4  ClickScreenIdentity.cs
//
//  点击热区的“稳定选屏”。Screen.AllScreens 的下标会随拔插/重排变化,不能作为
//  身份。这里用纯函数解析:
//    1) DeviceName 精确匹配
//    2) 边界矩形精确匹配
//    3) 宽高相同 + 中心点最近
//    4) 主屏
//    5) 第一块屏
//
//  不依赖 Screen 类型,因此单元测试可以直接喂假屏幕;不读文件、不查数据库。
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal struct ScreenRef
    {
        public string DeviceName;
        public Rectangle Bounds;
        public bool Primary;

        internal ScreenRef(string deviceName, Rectangle bounds, bool primary)
        {
            DeviceName = deviceName ?? "";
            Bounds = bounds;
            Primary = primary;
        }
    }

    internal static class ClickScreenIdentity
    {
        internal static int Resolve(string device, Rectangle hintBounds, ScreenRef[] screens)
        {
            if (screens == null || screens.Length == 0) return 0;

            if (!string.IsNullOrEmpty(device))
            {
                for (int i = 0; i < screens.Length; i++)
                {
                    if (string.Equals(screens[i].DeviceName ?? "", device,
                        StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            if (hintBounds.Width > 0 && hintBounds.Height > 0)
            {
                for (int i = 0; i < screens.Length; i++)
                    if (screens[i].Bounds == hintBounds) return i;

                int best = -1;
                double bestDistance = double.MaxValue;
                for (int i = 0; i < screens.Length; i++)
                {
                    Rectangle bounds = screens[i].Bounds;
                    if (bounds.Width != hintBounds.Width || bounds.Height != hintBounds.Height)
                        continue;
                    double dx = (bounds.Left + bounds.Width / 2.0) - (hintBounds.Left + hintBounds.Width / 2.0);
                    double dy = (bounds.Top + bounds.Height / 2.0) - (hintBounds.Top + hintBounds.Height / 2.0);
                    double distance = dx * dx + dy * dy;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }
                if (best >= 0) return best;
            }

            for (int i = 0; i < screens.Length; i++)
                if (screens[i].Primary) return i;
            return 0;
        }

        internal static ScreenRef[] Capture(Screen[] screens)
        {
            if (screens == null || screens.Length == 0) return new ScreenRef[0];
            ScreenRef[] result = new ScreenRef[screens.Length];
            for (int i = 0; i < screens.Length; i++)
                result[i] = new ScreenRef(screens[i].DeviceName, screens[i].Bounds, screens[i].Primary);
            return result;
        }

        /// <summary>用于缓存签名/底板 stamp 的稳定哈希(不用于持久化)。</summary>
        internal static long Hash(string device, Rectangle bounds)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                string text = device ?? "";
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 1099511628211L;
                }
                hash ^= bounds.X; hash *= 1099511628211L;
                hash ^= bounds.Y; hash *= 1099511628211L;
                hash ^= bounds.Width; hash *= 1099511628211L;
                hash ^= bounds.Height; hash *= 1099511628211L;
                return hash;
            }
        }
    }
}
