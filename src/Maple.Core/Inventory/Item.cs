namespace Maple.Core.Inventory;

/// <summary>
/// 執行期道具（可堆疊）。富領域型別，掛 <see cref="World.Player"/>（非持久——持久走扁平 <see cref="ItemRecord"/>）。
/// </summary>
public class Item
{
    public int ItemId { get; set; }
    public short Slot { get; set; }
    public short Quantity { get; set; } = 1;
    public string Owner { get; set; } = string.Empty;
    public long Expiration { get; set; } = -1;
    public short Flag { get; set; }
    public long UniqueId { get; set; }

    public virtual bool IsEquip => false;
}

/// <summary>
/// 執行期裝備（qty 恆 1）。MVP-0 渲染零屬性（RawEquip，見設計 doc 風險#3）；屬性欄位保留供 MVP-1+ 與 WZ 接入。
/// </summary>
public sealed class Equip : Item
{
    public override bool IsEquip => true;

    public byte UpgradeSlots { get; set; }
    public byte Level { get; set; }
    public byte ItemLevel { get; set; }
    public int ItemExp { get; set; }

    public short Str { get; set; }
    public short Dex { get; set; }
    public short Int { get; set; }
    public short Luk { get; set; }
    public short Hp { get; set; }
    public short Mp { get; set; }
    public short Watk { get; set; }
    public short Matk { get; set; }
    public short Wdef { get; set; }
    public short Mdef { get; set; }
    public short Acc { get; set; }
    public short Avoid { get; set; }
    public short Hands { get; set; }
    public short Speed { get; set; }
    public short Jump { get; set; }
}
