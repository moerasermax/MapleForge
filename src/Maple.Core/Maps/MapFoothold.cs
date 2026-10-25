namespace Maple.Core.Maps;

/// <summary>
/// 地圖踏腳石（foothold）：定義玩家可站立的平台/地面（對照舊 MapleFoothold）。
/// </summary>
public sealed class MapFoothold
{
    public int Id { get; init; }
    public int X1 { get; init; }
    public int Y1 { get; init; }
    public int X2 { get; init; }
    public int Y2 { get; init; }
    public int Next { get; init; }
    public int Prev { get; init; }

    /// <summary>是否為地面（非牆壁：y1==y2）。</summary>
    public bool IsFloor => Y1 == Y2;
}

/// <summary>
/// P128：踏腳石查詢（對照 Java <c>MapleFootholdTree.findBelow</c>／<c>MapleMap.calcFhAndPointBelow</c>）。
/// Java 先取四分樹 relevants 再以 <c>MapleFoothold.compareTo</c>（非全序，近似依 y 由上而下）排序；
/// MapleForge 對整張圖的踏腳石依 Y1 穩定排序後套用同一判斷。
/// </summary>
public static class FootholdMath
{
    public static MapFoothold? FindBelow(IReadOnlyList<MapFoothold> footholds, int x, int y)
    {
        foreach (var fh in footholds.Where(f => f.X1 <= x && f.X2 >= x).OrderBy(static f => f.Y1))
        {
            var isWall = fh.X1 == fh.X2;
            if (!isWall && fh.Y1 != fh.Y2)
            {
                if (SlopeY(fh, x) >= y)
                {
                    return fh;
                }
            }
            else if (!isWall && fh.Y1 >= y)
            {
                return fh;
            }
        }

        return null;
    }

    /// <summary>Java <c>calcFhAndPointBelow</c>：回傳 (踏腳石 id, x, 落點 y)；找不到回 null。</summary>
    public static (int FootholdId, int X, int Y)? CalcPointBelow(IReadOnlyList<MapFoothold> footholds, int x, int y)
    {
        var fh = FindBelow(footholds, x, y);
        if (fh is null)
        {
            return null;
        }

        var dropY = fh.Y1;
        if (fh.X1 != fh.X2 && fh.Y1 != fh.Y2)
        {
            dropY = SlopeY(fh, x);
        }

        return (fh.Id, x, dropY);
    }

    /// <summary>P144：Java <c>MapleMap.calcDropPos(initial, fallback)</c>：<c>calcPointBelow(x, y - 50)</c>，找不到用 fallback。</summary>
    public static (int X, int Y, int FootholdId)? CalcDropPos(IReadOnlyList<MapFoothold> footholds, int x, int y)
        => CalcPointBelow(footholds, x, y - 50) is { } point ? (point.X, point.Y, point.FootholdId) : null;

    /// <summary>
    /// P144：Java <c>MapleFootholdTree.getMinDropX/getMaxDropX</c>：根節點逐一 insert 時以 x1/x2 更新，欄位初值 0
    /// （範圍恆含 0，照抄）。
    /// </summary>
    public static (int Min, int Max) DropXRange(IReadOnlyList<MapFoothold> footholds)
    {
        int min = 0, max = 0;
        foreach (var fh in footholds)
        {
            max = Math.Max(max, Math.Max(fh.X1, fh.X2));
            min = Math.Min(min, Math.Min(fh.X1, fh.X2));
        }

        return (min, max);
    }

    // Java：s1=|y2-y1|、s2=|x2-x1|、s4=|x-x1|，s5 = cos(atan(s2/s1)) * (s4 / cos(atan(s1/s2)))；y2<y1 時往上。
    private static int SlopeY(MapFoothold fh, int x)
    {
        double s1 = Math.Abs(fh.Y2 - fh.Y1);
        double s2 = Math.Abs(fh.X2 - fh.X1);
        double s4 = Math.Abs(x - fh.X1);
        var s5 = Math.Cos(Math.Atan(s2 / s1)) * (s4 / Math.Cos(Math.Atan(s1 / s2)));
        return fh.Y2 < fh.Y1 ? fh.Y1 - (int)s5 : fh.Y1 + (int)s5;
    }
}
