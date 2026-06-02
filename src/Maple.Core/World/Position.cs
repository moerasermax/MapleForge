namespace Maple.Core.World;

/// <summary>
/// 地圖上的執行期位置（值物件）。對照舊 OdinMS 的 Point + stance(朝向/動作) + foothold。
/// 不可變；移動套用＝產生新 Position。
/// </summary>
public readonly record struct Position(short X, short Y, byte Stance, short Foothold)
{
    /// <summary>與另一點的歐氏距離（供戰鬥/NPC/技能範圍判定）。</summary>
    public double DistanceTo(Position other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
