namespace Maple.Core.Maps;

/// <summary>
/// 地圖上的靜態 NPC 定義（從 WZ <c>life</c> 節點 type=&quot;n&quot; 載入；不可變）。
/// 對照舊 OdinMS：<c>MapleMapFactory.loadLife</c> 讀 life 子節點 → <c>MapleNPC</c>。
/// 執行期狀態（objectId / 是否被控制）不在此，落在 Core/World <c>Npc</c>。
/// </summary>
public sealed class MapNpc
{
    /// <summary>NPC 模板 id（WZ Npc.wz / String.wz 的鍵）。</summary>
    public int NpcId { get; init; }

    public int X { get; init; }

    /// <summary>站立 y（WZ <c>cy</c>，spawn 封包用，非腳底 y）。</summary>
    public int Cy { get; init; }

    /// <summary>朝向（WZ <c>f</c>，0/1；spawn 封包送 f==1?0:1）。</summary>
    public int F { get; init; }

    /// <summary>所在 foothold id。</summary>
    public int Fh { get; init; }

    /// <summary>水平移動範圍左界。</summary>
    public int Rx0 { get; init; }

    /// <summary>水平移動範圍右界。</summary>
    public int Rx1 { get; init; }

    /// <summary>隱藏 NPC（WZ <c>hide</c>=1）；不送 spawn。</summary>
    public bool Hide { get; init; }
}
