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
