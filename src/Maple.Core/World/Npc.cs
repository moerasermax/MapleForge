using Maple.Core.Maps;

namespace Maple.Core.World;

/// <summary>
/// 執行期 NPC（地圖物件）：以**組合**持有靜態 <see cref="MapNpc"/> 定義 + 場上配發的 ObjectId。
/// 對照舊 OdinMS <c>MapleNPC</c>（去掉 static factory/shop 耦合，只留地圖物件身分）。
/// NPC 位置在生命週期內固定（移動型 NPC 由客戶端依 rx0/rx1 自行擺動，server 不追蹤）。
/// </summary>
public sealed class Npc : IFieldObject
{
    /// <summary>靜態 NPC 定義（WZ）。</summary>
    public MapNpc Definition { get; }

    /// <summary>場上唯一物件 id（由 Field 配發；NPC 對話/控制器以此為 handle）。</summary>
    public int ObjectId { get; }

    public Position Position { get; }

    public FieldObjectType Type => FieldObjectType.Npc;

    public Npc(MapNpc definition, int objectId)
    {
        Definition = definition;
        ObjectId = objectId;
        Position = new Position((short)definition.X, (short)definition.Cy, 0, (short)definition.Fh);
    }
}
