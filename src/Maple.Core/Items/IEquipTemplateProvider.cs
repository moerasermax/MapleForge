using Maple.Core.Inventory;

namespace Maple.Core.Items;

/// <summary>
/// 裝備預設數值來源（對照 Java <c>MapleItemInformationProvider.getEquipById</c>）：
/// 回傳該裝備 ID 的全新副本（可自由修改），數值取自資料檔；查不到資料時回只有 ItemId 的空裝備。
/// 不含 Java 掉落時另套的 <c>randomizeStats</c>（見 <see cref="EquipStatRandomizer"/>）。
/// </summary>
public interface IEquipTemplateProvider
{
    Equip Create(int itemId);

    /// <summary>P154：Java <c>MapleItemInformationProvider.isCash</c>（裝備 info/cash 存在）；預設 false。</summary>
    bool IsCash(int itemId) => false;

    /// <summary>P168：Java <c>MapleItemInformationProvider.itemExists</c>（Item.wz／Character.wz 有該 ID 節點）；預設 true。</summary>
    bool ItemExists(int itemId) => true;

    /// <summary>
    /// P181：Java <c>getSlotMax</c>（WZ info/slotMax；缺值時裝備 1、其他 100——DumpItems 預設）。
    /// 預設實作只給缺值預設；<see cref="ItemSlotMax.Resolve"/> 先套 GameConstants 的固定值。
    /// </summary>
    short GetSlotMax(int itemId) => ItemSlotMax.Default(itemId);
}

/// <summary>P181：道具堆疊上限規則（Java GameConstants.getSlotMax 固定值 → WZ info/slotMax → 預設）。</summary>
public static class ItemSlotMax
{
    /// <summary>Java DumpItems：沒有 info/slotMax 時裝備 1、其餘 100。</summary>
    public static short Default(int itemId) => itemId / 1_000_000 == 1 ? (short)1 : (short)100;

    /// <summary>Java <c>GameConstants.getSlotMax</c>：特定道具固定上限（0 = 不覆寫）。</summary>
    public static short Override(int itemId) => itemId switch
    {
        4030003 or 4030004 or 4030005 => 1,
        4001168 or 4031306 or 4031307 or 3993000 or 3993002 or 3993003 => 100,
        5220010 or 5220013 => 1000,
        5220020 => 2000,
        _ => 0,
    };

    public static short Resolve(IEquipTemplateProvider? provider, int itemId)
        => Override(itemId) is > 0 and var fixedMax ? fixedMax : provider?.GetSlotMax(itemId) ?? Default(itemId);
}
