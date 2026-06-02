namespace Maple.Core.World;

/// <summary>地圖物件種類（玩家/NPC/怪物/掉落/召喚…）。</summary>
public enum FieldObjectType
{
    Player,
    Npc,
    Monster,
    Drop,
    Summon,
}

/// <summary>
/// 地圖上的可定位物件共同介面（玩家/NPC/怪/掉落都實作）。
/// 讓範圍查詢、鎖定、廣播能以同一套機制處理所有場上物件。
/// </summary>
public interface IFieldObject
{
    /// <summary>地圖物件 id（同一 FieldInstance 內唯一）。玩家以角色 id 充當；怪/NPC/掉落另配發。</summary>
    int ObjectId { get; }

    /// <summary>當前位置。</summary>
    Position Position { get; }

    /// <summary>物件種類。</summary>
    FieldObjectType Type { get; }
}
