using Kms.App.Layout;
using Kms.App.Widgets;

namespace Kms.Golden.Tests;

[Collection("page-golden")]
public sealed class BentoLayoutMathTests
{
    [Fact]
    public void FindFirstFit_ReturnsTopLeftOnEmptyGrid()
    {
        Assert.True(BentoLayoutMath.TryFindFirstFit([], new WidgetSpan(6, 3), out int col, out int row));
        Assert.Equal(0, col);
        Assert.Equal(0, row);
    }

    [Fact]
    public void FindFirstFit_SkipsOccupiedRows()
    {
        List<WidgetPlacement> placements =
        [
            Place("overview.kpi.keys", 0, 0, 12, 1)
        ];

        Assert.True(BentoLayoutMath.TryFindFirstFit(placements, new WidgetSpan(6, 3), out int col, out int row));
        Assert.Equal(0, col);
        Assert.Equal(1, row);
    }

    [Fact]
    public void FindFirstFit_ReturnsFalseWhenNoSpaceRemains()
    {
        List<WidgetPlacement> placements =
        [
            Place("overview.distribution_ring", 0, 0, 12, 6)
        ];

        Assert.False(BentoLayoutMath.TryFindFirstFit(placements, new WidgetSpan(3, 1), out _, out _));
    }

    [Fact]
    public void MoveOrSwap_SwapsTwoSameSizeCards()
    {
        List<WidgetPlacement> placements =
        [
            Place("a", 0, 0, 6, 3),
            Place("b", 6, 0, 6, 3)
        ];

        Assert.True(BentoLayoutMath.TryMoveOrSwap(placements, "a", 6, 0, out List<WidgetPlacement> updated, out string? error), error);
        Assert.Equal(6, updated.Single(item => item.ModuleId == "a").Col);
        Assert.Equal(0, updated.Single(item => item.ModuleId == "b").Col);
        Assert.False(LayoutValidation.HasOverlap(updated));
    }

    [Fact]
    public void MoveOrSwap_RejectsDifferentSizedCollision()
    {
        List<WidgetPlacement> placements =
        [
            Place("a", 0, 0, 6, 3),
            Place("b", 6, 0, 4, 3)
        ];

        Assert.False(BentoLayoutMath.TryMoveOrSwap(placements, "a", 6, 0, out _, out _));
    }

    [Fact]
    public void Resize_DetectsCollisionAndCanRelocate()
    {
        List<WidgetPlacement> placements =
        [
            Place("a", 0, 0, 6, 3),
            Place("b", 6, 0, 6, 3)
        ];

        Assert.False(BentoLayoutMath.TryResize(placements, "a", new WidgetSpan(12, 3), false, out _, out _));
        Assert.True(BentoLayoutMath.TryResize(placements, "a", new WidgetSpan(12, 3), true, out List<WidgetPlacement> updated, out string? error), error);
        WidgetPlacement resized = updated.Single(item => item.ModuleId == "a");
        Assert.Equal(0, resized.Col);
        Assert.Equal(3, resized.Row);
        Assert.Equal(12, resized.ColSpan);
        Assert.False(LayoutValidation.HasOverlap(updated));
    }

    [Fact]
    public void Resize_RejectsSpanBeyondGrid()
    {
        List<WidgetPlacement> placements = [Place("a", 0, 0, 6, 3)];
        Assert.False(BentoLayoutMath.TryResize(placements, "a", new WidgetSpan(13, 3), true, out _, out _));
    }

    private static WidgetPlacement Place(
        string moduleId,
        int col,
        int row,
        int colSpan,
        int rowSpan) => new()
    {
        ModuleId = moduleId,
        Col = col,
        Row = row,
        ColSpan = colSpan,
        RowSpan = rowSpan
    };
}
