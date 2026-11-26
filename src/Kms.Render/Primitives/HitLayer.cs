using Avalonia;

namespace Kms.Render.Primitives;

/// <summary>
/// 一个命中目标。ZIndex 只影响命中优先级，不要求与绘制顺序一致。
/// </summary>
public sealed record HitTarget(
    string Id,
    Rect Bounds,
    object? Payload = null,
    int ZIndex = 0,
    bool Enabled = true);

/// <summary>
/// 绘制前登记命中区，绘制本身可以按任意顺序进行，命中按 ZIndex/登记顺序决定。
/// 同 ZIndex 时后登记者优先（等价于“后画在上”），但调用方无需按绘制顺序登记。
/// </summary>
public sealed class HitLayer
{
    private readonly List<HitTarget> _targets = [];

    public IReadOnlyList<HitTarget> Targets => _targets;

    public int Count => _targets.Count;

    public void Clear() => _targets.Clear();

    public HitTarget Add(string id, Rect bounds, object? payload = null, int zIndex = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        HitTarget target = new(id, bounds, payload, zIndex);
        _targets.Add(target);
        return target;
    }

    public void Add(HitTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Id);
        _targets.Add(target);
    }

    public bool Remove(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_targets[i].Id, id, StringComparison.Ordinal))
            {
                _targets.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>返回最上层命中；没有命中返回 null。空矩形与禁用项不参与命中。</summary>
    public HitTarget? HitTest(Point point)
    {
        HitTarget? best = null;
        int bestZ = int.MinValue;

        for (int i = 0; i < _targets.Count; i++)
        {
            HitTarget target = _targets[i];
            if (!target.Enabled || target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
            {
                continue;
            }

            if (!target.Bounds.Contains(point))
            {
                continue;
            }

            // >= 让同 ZIndex 的后登记项胜出；与绘制顺序无关。
            if (best is null || target.ZIndex >= bestZ)
            {
                best = target;
                bestZ = target.ZIndex;
            }
        }

        return best;
    }

    /// <summary>命中栈，从最上层到最下层，便于调试与逐层下钻。</summary>
    public IReadOnlyList<HitTarget> HitTestAll(Point point)
    {
        List<(HitTarget Target, int Order)> hits = [];

        for (int i = 0; i < _targets.Count; i++)
        {
            HitTarget target = _targets[i];
            if (!target.Enabled || target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
            {
                continue;
            }

            if (target.Bounds.Contains(point))
            {
                hits.Add((target, i));
            }
        }

        hits.Sort(static (a, b) =>
        {
            int byZ = b.Target.ZIndex.CompareTo(a.Target.ZIndex);
            return byZ != 0 ? byZ : b.Order.CompareTo(a.Order);
        });

        HitTarget[] result = new HitTarget[hits.Count];
        for (int i = 0; i < hits.Count; i++)
        {
            result[i] = hits[i].Target;
        }

        return result;
    }
}
