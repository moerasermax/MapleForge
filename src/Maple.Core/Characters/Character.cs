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
    public const int EmptyRockMapId = 999999999;
    private const int RegularRockSlotCount = 5;
    private const int VipRockSlotCount = 10;

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

    public long LastFameAtUnixMillis { get; set; }

    public List<FameRecord> FameHistory { get; set; } = new();

    public int GachExp { get; set; }

    /// <summary>角色資訊頁個人訊息（對照 Java charmessage）。</summary>
    public string CharacterMessage { get; set; } = string.Empty;

    /// <summary>角色資訊頁表情設定（對照 Java expression）。</summary>
    public byte ProfileExpression { get; set; }

    public byte Constellation { get; set; }

    public byte Blood { get; set; }

    public byte BirthMonth { get; set; }

    public byte BirthDay { get; set; }

    /// <summary>寵物自動 HP 補藥 item id；0 = 未設定。</summary>
    public int PetAutoHpItemId { get; set; }

    /// <summary>寵物自動 MP 補藥 item id；0 = 未設定。</summary>
    public int PetAutoMpItemId { get; set; }

    /// <summary>普通傳送石地圖清單，固定 5 格；空格對照 Java 999999999。</summary>
    public List<int> RegularRocks { get; set; } = EmptyRockSlots(RegularRockSlotCount);

    /// <summary>VIP 傳送石地圖清單，固定 10 格；空格對照 Java 999999999。</summary>
    public List<int> VipRocks { get; set; } = EmptyRockSlots(VipRockSlotCount);

    /// <summary>角色技能等級清單；SET_FIELD 的 skill info 由版本 adapter 編碼。</summary>
    public List<CharacterSkillRecord> Skills { get; set; } = new();

    /// <summary>玩家鍵盤快捷鍵設定；v113 adapter 會在登入時編碼成 90 格 keymap。</summary>
    public List<KeyBindingRecord> Keymap { get; set; } = new();

    /// <summary>玩家技能宏設定，最多 5 組；v113 adapter 會依 position 編碼。</summary>
    public List<SkillMacroRecord> SkillMacros { get; set; } = new();

    /// <summary>怪物書封面卡片 item id；0 = 未設定。對照 Java characters.mBookCover。</summary>
    public int MonsterBookCover { get; set; }

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

    /// <summary>公會 id（對照 Java characters.guildid；0 = 無公會）。</summary>
    public int GuildId { get; set; }

    /// <summary>公會階級（對照 Java characters.guildrank；1 = 會長，5 = 一般成員）。</summary>
    public byte GuildRank { get; set; } = 5;

    /// <summary>聯盟階級（對照 Java characters.allianceRank；未加入聯盟預設 5）。</summary>
    public byte AllianceRank { get; set; } = 5;

    public void ChangeKeyBinding(int key, byte type, int action)
    {
        var index = Keymap.FindIndex(k => k.Key == key);
        if (type == 0)
        {
            if (index >= 0)
            {
                Keymap.RemoveAt(index);
            }
            return;
        }

        if (index >= 0)
        {
            Keymap[index].Type = type;
            Keymap[index].Action = action;
            return;
        }

        Keymap.Add(new KeyBindingRecord
        {
            Key = key,
            Type = type,
            Action = action,
        });
    }

    public void UpdateSkillMacro(int position, string name, byte shout, int skill1, int skill2, int skill3)
    {
        if (position is < 0 or >= 5)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Skill macro position must be 0..4.");
        }

        var index = SkillMacros.FindIndex(m => m.Position == position);
        if (index >= 0)
        {
            SkillMacros[index].Name = name;
            SkillMacros[index].Shout = shout;
            SkillMacros[index].Skill1 = skill1;
            SkillMacros[index].Skill2 = skill2;
            SkillMacros[index].Skill3 = skill3;
            return;
        }

        SkillMacros.Add(new SkillMacroRecord
        {
            Position = position,
            Name = name,
            Shout = shout,
            Skill1 = skill1,
            Skill2 = skill2,
            Skill3 = skill3,
        });
    }

    public void ChangeMonsterBookCover(int coverItemId)
    {
        if (coverItemId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(coverItemId), coverItemId, "Monster book cover must be 0 or a monster card item id.");
        }

        MonsterBookCover = coverItemId;
    }

    public void UpdateCharacterMessage(string message) => CharacterMessage = message;

    public void UpdateProfileExpression(byte expression) => ProfileExpression = expression;

    public void UpdateProfileBirthday(byte blood, byte month, byte day, byte constellation)
    {
        Blood = blood;
        BirthMonth = month;
        BirthDay = day;
        Constellation = constellation;
    }

    public bool UpdatePetAutoPot(int type, int itemId)
    {
        var normalizedItemId = itemId > 0 ? itemId : 0;
        switch (type)
        {
            case 1:
                PetAutoHpItemId = normalizedItemId;
                return true;
            case 2:
                PetAutoMpItemId = normalizedItemId;
                return true;
            default:
                return false;
        }
    }

    public IReadOnlyList<int> GetRegularRockSlots()
    {
        RegularRocks = NormalizeRockSlots(RegularRocks, RegularRockSlotCount);
        return RegularRocks;
    }

    public IReadOnlyList<int> GetVipRockSlots()
    {
        VipRocks = NormalizeRockSlots(VipRocks, VipRockSlotCount);
        return VipRocks;
    }

    public bool AddRegularRock(int mapId)
    {
        RegularRocks = NormalizeRockSlots(RegularRocks, RegularRockSlotCount);
        return AddRockMap(RegularRocks, mapId);
    }

    public bool AddVipRock(int mapId)
    {
        VipRocks = NormalizeRockSlots(VipRocks, VipRockSlotCount);
        return AddRockMap(VipRocks, mapId);
    }

    public bool RemoveRegularRock(int mapId)
    {
        RegularRocks = NormalizeRockSlots(RegularRocks, RegularRockSlotCount);
        return RemoveRockMap(RegularRocks, mapId);
    }

    public bool RemoveVipRock(int mapId)
    {
        VipRocks = NormalizeRockSlots(VipRocks, VipRockSlotCount);
        return RemoveRockMap(VipRocks, mapId);
    }

    private static List<int> EmptyRockSlots(int count)
        => Enumerable.Repeat(EmptyRockMapId, count).ToList();

    private static List<int> NormalizeRockSlots(IEnumerable<int>? slots, int count)
    {
        var result = slots?.Take(count).ToList() ?? [];
        while (result.Count < count)
        {
            result.Add(EmptyRockMapId);
        }

        return result;
    }

    private static bool AddRockMap(List<int> slots, int mapId)
    {
        if (mapId <= 0 || mapId == EmptyRockMapId || slots.Contains(mapId))
        {
            return false;
        }

        var index = slots.FindIndex(static map => map == EmptyRockMapId);
        if (index < 0)
        {
            return false;
        }

        slots[index] = mapId;
        return true;
    }

    private static bool RemoveRockMap(List<int> slots, int mapId)
    {
        var index = slots.FindIndex(map => map == mapId);
        if (index < 0)
        {
            return false;
        }

        slots[index] = EmptyRockMapId;
        return true;
    }
}
