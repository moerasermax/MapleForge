using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.Items;

namespace Maple.Core.World;

/// <summary>
/// 入世玩家（執行期領域實體）：以**組合**持有持久 <see cref="Characters.Character"/> + 執行期位置/狀態，
/// 而非擴充 Character（持久文件不被每 tick 變動污染）。Core 富領域、**零傳輸**（不持 socket/送包委派）。
/// 之後擴充：即時 HP/MP(Vitals)、buff、行為(TakeDamage/Heal/ApplyBuff…)。
/// </summary>
public sealed partial class Player : IFieldObject
{
    /// <summary>持久角色資料（唯一在存檔/換圖/登出時由 Player 回寫）。</summary>
    public Character Character { get; }

    /// <summary>執行期位置（唯一位置真相；session 中途不從 Character 讀）。</summary>
    public Position Position { get; private set; }

    /// <summary>玩家以角色 id 充當地圖物件 id（怪/NPC/掉落由 FieldInstance 另配發）。</summary>
    public int ObjectId => Character.Id;

    public FieldObjectType Type => FieldObjectType.Player;

    /// <summary>執行期富背包（由 Character.Items hydrate；所有變動經 Player，checkpoint 時 flush 回）。</summary>
    public Inventories Inventory { get; }

    /// <summary>目前坐著的椅子 id；0 = 未坐椅子。此為 session runtime 狀態，不持久化到 Character。</summary>
    public int ChairItemId { get; private set; }

    /// <summary>目前顯示中的道具效果 id；0 = 無。此為 session runtime 狀態，不持久化到 Character。</summary>
    public int ItemEffectItemId { get; private set; }

    /// <summary>目前黑板文字；空字串 = 未顯示。此為 session runtime 狀態，不持久化到 Character。</summary>
    public string ChalkboardMessage { get; private set; } = string.Empty;

    private readonly IEquipTemplateProvider? _equipTemplates;

    public Player(Character character, Position spawn, IEquipTemplateProvider? equipTemplates = null)
    {
        Character = character;
        Position = spawn;
        Inventory = Inventories.Hydrate(character.Items);
        _equipTemplates = equipTemplates;
    }

    /// <summary>
    /// 產生新裝備（對照 Java getEquipById）：有模板來源時帶資料檔預設數值，否則只有 ItemId 的空裝備。
    /// 每次回傳全新實例，呼叫端可自由改 Owner/Expiration 等。
    /// </summary>
    private Equip NewEquip(int itemId)
        => _equipTemplates?.Create(itemId) ?? new Equip { ItemId = itemId, Quantity = 1 };

    /// <summary>套用移動最終位置（由 Application 用例在解析客戶端移動後呼叫）。</summary>
    public void MoveTo(Position position) => Position = position;

    /// <summary>
    /// 增減楓幣（meso）。富領域不變式：餘額不為負（扣超過持有時夾到 0）、不溢位。
    /// NPC 腳本 cm.gainMeso 等行為經此入口，而非直接寫 Character.Meso。
    /// </summary>
    public void GainMeso(int delta)
    {
        var next = (long)Character.Meso + delta;
        if (next < 0) next = 0;
        if (next > int.MaxValue) next = int.MaxValue;
        Character.Meso = (int)next;
    }

    /// <summary>取得道具到背包（cm.gainItem 入口）。回傳放入的 Item；背包滿回 null。裝備依 id 範圍判定。</summary>
    public Item? GainItem(InventoryType type, int itemId, short quantity = 1)
    {
        var isEquip = type == InventoryType.Equip;
        Item item = isEquip
            ? NewEquip(itemId)
            : new Item { ItemId = itemId, Quantity = quantity };
        return Inventory.By(type).Gain(item);
    }

    /// <summary>P191：Java getBossLog：該 bossId 在過去 24 小時（subtime(now, '1 0:0:0')）內的筆數。</summary>
    public int GetBossLog(string bossId, DateTimeOffset now)
    {
        var since = now.AddDays(-1).ToUnixTimeMilliseconds();
        return Character.BossLog.Count(e => e.BossId == bossId && e.Timestamp >= since);
    }

    /// <summary>P191：Java setBossLog：新增一筆（順便清掉超過 24 小時、已不影響計數的舊紀錄）。</summary>
    public void SetBossLog(string bossId, DateTimeOffset now)
    {
        var since = now.AddDays(-1).ToUnixTimeMilliseconds();
        Character.BossLog.RemoveAll(e => e.Timestamp < since);
        Character.BossLog.Add(new BossLogEntry { BossId = bossId, Timestamp = now.ToUnixTimeMilliseconds() });
    }

    /// <summary>P191：Java getOneTimeLog／setOneTimeLog（累計次數）。</summary>
    public int GetOneTimeLog(string log) => Character.OneTimeLog.GetValueOrDefault(log);

    public void SetOneTimeLog(string log) => Character.OneTimeLog[log] = GetOneTimeLog(log) + 1;

    /// <summary>P181：Java <c>getSlotMax</c>。</summary>
    public short GetSlotMax(int itemId) => Maple.Core.Items.ItemSlotMax.Resolve(_equipTemplates, itemId);

    /// <summary>
    /// P181：Java <c>addById(c, itemId, quantity[, owner=null, period])</c>：只合併到沒有期限的堆疊；
    /// 新格帶 <paramref name="expiration"/>（-1 = 永久）。裝備依模板建立。
    /// </summary>
    public InventoryStackGain GainItemStacking(int itemId, short quantity, long expiration = -1)
    {
        var type = InventoryTypeOf(itemId);
        Item item = type == InventoryType.Equip
            ? NewEquip(itemId)
            : new Item { ItemId = itemId, Quantity = quantity };
        item.Expiration = expiration;
        return Inventory.By(type).GainStacking(item, GetSlotMax(itemId), static existing => existing.Expiration == -1);
    }

    /// <summary>P181：Java <c>checkSpace(c, itemId, quantity, "")</c>。</summary>
    public bool HasSpaceFor(int itemId, int quantity, string owner = "")
        => Inventory.By(InventoryTypeOf(itemId)).HasSpaceFor(itemId, quantity, GetSlotMax(itemId), owner);

    /// <summary>背包是否持有指定道具（cm.haveItem 入口）。</summary>
    public bool HasItem(InventoryType type, int itemId) => Inventory.By(type).CountById(itemId) > 0;

    public void UseChair(int itemId) => ChairItemId = itemId;

    public void CancelChair() => ChairItemId = 0;

    public void UseMapChair(short chairId) => ChairItemId = chairId;

    public void UseItemEffect(int itemId) => ItemEffectItemId = itemId;

    public void CancelItemEffect() => ItemEffectItemId = 0;

    public void SetChalkboard(string? message) => ChalkboardMessage = message ?? string.Empty;

    public void ClearChalkboard() => ChalkboardMessage = string.Empty;

    public void UpdateCharacterMessage(string message) => Character.UpdateCharacterMessage(message);

    public void UpdateProfileExpression(byte expression) => Character.UpdateProfileExpression(expression);

    public void UpdateProfileBirthday(byte blood, byte month, byte day, byte constellation)
        => Character.UpdateProfileBirthday(blood, month, day, constellation);

    public bool UpdatePetAutoPot(int type, int itemId) => Character.UpdatePetAutoPot(type, itemId);

    public bool AddRegularRock(int mapId) => Character.AddRegularRock(mapId);

    public bool AddVipRock(int mapId) => Character.AddVipRock(mapId);

    public bool RemoveRegularRock(int mapId) => Character.RemoveRegularRock(mapId);

    public bool RemoveVipRock(int mapId) => Character.RemoveVipRock(mapId);

    public bool IsRegularRockMap(int mapId) => Character.IsRegularRockMap(mapId);

    public bool IsVipRockMap(int mapId) => Character.IsVipRockMap(mapId);

    public void ChangeKeyBinding(int key, byte type, int action) => Character.ChangeKeyBinding(key, type, action);

    public void UpdateSkillMacro(int position, string name, byte shout, int skill1, int skill2, int skill3)
        => Character.UpdateSkillMacro(position, name, shout, skill1, skill2, skill3);

    public void ChangeMonsterBookCover(int coverItemId) => Character.ChangeMonsterBookCover(coverItemId);

    /// <summary>格內移動道具（ITEM_MOVE 入口；變動唯一經 Player）。</summary>
    public bool MoveItem(InventoryType type, short src, short dst) => Inventory.By(type).Move(src, dst);

    public bool GatherInventory(InventoryType type) => Inventory.By(type).GatherByItemId();

    public bool SortInventory(InventoryType type) => Inventory.By(type).SortByItemId();

    /// <summary>把執行期背包 flush 回 Character.Items（checkpoint/換圖/登出時呼叫）。</summary>
    public void FlushInventory() => Character.Items = Inventory.Flush();
}
