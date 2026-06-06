using Maple.Core.Characters;
using Maple.Core.Inventory;

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

    public Player(Character character, Position spawn)
    {
        Character = character;
        Position = spawn;
        Inventory = Inventories.Hydrate(character.Items);
    }

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
            ? new Equip { ItemId = itemId, Quantity = 1 }
            : new Item { ItemId = itemId, Quantity = quantity };
        return Inventory.By(type).Gain(item);
    }

    /// <summary>背包是否持有指定道具（cm.haveItem 入口）。</summary>
    public bool HasItem(InventoryType type, int itemId) => Inventory.By(type).CountById(itemId) > 0;

    /// <summary>格內移動道具（ITEM_MOVE 入口；變動唯一經 Player）。</summary>
    public bool MoveItem(InventoryType type, short src, short dst) => Inventory.By(type).Move(src, dst);

    /// <summary>把執行期背包 flush 回 Character.Items（checkpoint/換圖/登出時呼叫）。</summary>
    public void FlushInventory() => Character.Items = Inventory.Flush();
}
