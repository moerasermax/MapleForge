namespace Maple.Core.Maps;

/// <summary>
/// 地圖上的靜態怪物出生定義（從 WZ <c>life</c> 節點 type=&quot;m&quot; 載入；不可變）。
/// 執行期 HP / objectId / 死亡狀態落在 Core/World <c>Mob</c>。
/// </summary>
public sealed class MapMonster
{
    /// <summary>怪物模板 id（WZ Mob.wz 的鍵）。</summary>
    public int MonsterId { get; init; }

    public int X { get; init; }

    /// <summary>WZ <c>y</c>，怪物 spawn 封包使用的實際位置 y。</summary>
    public int Y { get; init; }

    /// <summary>WZ <c>cy</c>，保留給後續重生/落地修正。</summary>
    public int Cy { get; init; }

    public int F { get; init; }

    public int Fh { get; init; }

    public int Rx0 { get; init; }

    public int Rx1 { get; init; }

    public int MobTime { get; init; }

    public sbyte Team { get; init; } = -1;

    public bool Hide { get; init; }
}
