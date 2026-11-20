using System.Collections.Generic;
using System.Drawing;

namespace KeyMouseStats
{
    /// <summary>
    /// 交互芯片登记门面:绘制时 Add(id, rect),命中测试只读同一份顺序表。
    /// 底层仍复用 Dashboard 现有的 _chips / _chipIds 列表,保证 Tab 顺序与旧测试反射兼容。
    /// </summary>
    internal sealed class ChipLedger
    {
        private readonly List<RectangleF> _rects;
        private readonly List<int> _ids;

        internal ChipLedger(List<RectangleF> rects, List<int> ids)
        {
            _rects = rects;
            _ids = ids;
        }

        internal void BeginFrame()
        {
            _rects.Clear();
            _ids.Clear();
        }

        internal void Add(int id, RectangleF rect)
        {
            _rects.Add(rect);
            _ids.Add(id);
        }

        internal bool TryHit(PointF point, out int id)
        {
            for (int i = 0; i < _rects.Count; i++)
            {
                if (!_rects[i].Contains(point)) continue;
                id = _ids[i];
                return true;
            }
            id = -1;
            return false;
        }
    }
}
