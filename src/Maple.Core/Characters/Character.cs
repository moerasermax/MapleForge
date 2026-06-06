using Maple.Core.Inventory;
using Maple.Core.Quests;
using Maple.Core.Skills;

namespace Maple.Core.Characters;

/// <summary>
/// 角色文件模型（LiteDB 集合根文件）。
/// 採文件模型：一份文件代表一個角色的完整狀態，Load/Save 原子單元。
/// </summary>
public sealed class Character
{
    /// <summary>LiteDB 自動遞增主鍵。</summary>
    public int Id { get; set; }

    public int AccountId { get; set; }

    /// <summary>角色名稱（全 DB 唯一索引）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>0=男, 1=女。</summary>
    public byte Gender { get; set; }

    public byte SkinColor { get; set; }

    public int Face { get; set; }

    public int Hair { get; set; }

    public byte Level { get; set; } = 1;

    /// <summary>職業 ID：0=初心者, 1000=皇家騎士, 2000=狂狼勇士。</summary>
    public short Job { get; set; }

    public CharacterStats Stats { get; set; } = new();

    public short RemainingAp { get; set; }

    public short RemainingSp { get; set; }

    public int Exp { get; set; }

    public short Fame { get; set; }

    public int GachExp { get; set; }

    /// <summary>角色技能等級清單；SET_FIELD 的 skill info 由版本 adapter 編碼。</summary>
    public List<CharacterSkillRecord> Skills { get; set; } = new();

    public int MapId { get; set; }

    public byte SpawnPoint { get; set; }

    /// <summary>持有楓幣（meso）。執行期由 <see cref="World.Player.GainMeso"/> 經富領域不變式變動。</summary>
    public int Meso { get; set; }

    /// <summary>穿戴中的裝備清單（對照 EQUIPPED 槽，負 slot，驅動 char look）。equip/unequip 統一收攏留 MVP-1。</summary>
    public List<EquipEntry> Equips { get; set; } = new();

    /// <summary>背包道具的持久扁平快照（正 slot，5 種背包）。執行期由 <see cref="World.Player"/> hydrate 成富 Inventories。</summary>
    public List<ItemRecord> Items { get; set; } = new();

    /// <summary>好友清單（容量 + entries）。Mongo/LiteDB 以整份 Character 文件持久化。</summary>
    public BuddyList BuddyList { get; set; } = new();

    /// <summary>任務進度快照（對照 Java MapleQuestStatus；Mongo/LiteDB 整份角色文件序列化自動帶出）。</summary>
    public List<QuestRecord> Quests { get; set; } = new();

    /// <summary>任務資訊字串（對照 Java MapleCharacter.questinfo / QuestInfoPacket）。</summary>
    public List<QuestInfoRecord> QuestInfo { get; set; } = new();
}
