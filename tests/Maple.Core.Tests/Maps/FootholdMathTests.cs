using Maple.Core.Maps;

namespace Maple.Core.Tests.Maps;

/// <summary>P128：對照 Java MapleFootholdTree.findBelow / MapleMap.calcFhAndPointBelow。</summary>
public sealed class FootholdMathTests
{
    private static readonly MapFoothold Upper = new() { Id = 1, X1 = -100, Y1 = 0, X2 = 100, Y2 = 0 };
    private static readonly MapFoothold Lower = new() { Id = 2, X1 = -300, Y1 = 200, X2 = 300, Y2 = 200 };
    private static readonly MapFoothold Wall = new() { Id = 3, X1 = 50, Y1 = -500, X2 = 50, Y2 = 500 };
    private static readonly MapFoothold Slope = new() { Id = 4, X1 = 400, Y1 = 100, X2 = 500, Y2 = 200 };

    [Fact]
    public void CalcPointBelow_PicksFirstFlatFootholdBelowPoint()
    {
        Assert.Equal((1, 0, 0), FootholdMath.CalcPointBelow([Lower, Upper, Wall], 0, -10));
        Assert.Equal((2, 0, 200), FootholdMath.CalcPointBelow([Lower, Upper, Wall], 0, 10));
    }

    [Fact]
    public void CalcPointBelow_IgnoresWalls()
    {
        Assert.Equal((2, 50, 200), FootholdMath.CalcPointBelow([Wall, Lower], 50, 10));
    }

    [Fact]
    public void CalcPointBelow_SlopeInterpolatesY()
    {
        // 45 度斜坡：x=450 幾何上是 150，但 Java 的 cos(atan)…double 運算得 49.999… 再 (int) 截斷成 49 → 149（照 Java）。
        Assert.Equal((4, 450, 149), FootholdMath.CalcPointBelow([Slope], 450, 0));
    }

    [Fact]
    public void CalcPointBelow_NothingBelow_Null()
    {
        Assert.Null(FootholdMath.CalcPointBelow([Upper], 0, 50));
        Assert.Null(FootholdMath.CalcPointBelow([Upper], 999, -50));
    }
}
