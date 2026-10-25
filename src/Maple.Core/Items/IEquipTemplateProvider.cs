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
}
