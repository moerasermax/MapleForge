using Maple.Core.Inventory;

namespace Maple.Core.Shops;

/// <summary>版本無關 NPC 商店品項。欄位對應 OdinMS shopitems：itemid/price/buyable/reqitem/reqitemq。</summary>
public sealed record ShopItem(
    int ItemId,
    int Price,
    short Buyable,
    int RequiredItemId,
    int RequiredItemQuantity,
    int SellPrice);

/// <summary>版本無關 NPC 商店定義。ShopId 對應 OdinMS shops.shopid，NpcId 對應 shops.npcid。</summary>
public sealed record ShopDefinition(int ShopId, int NpcId, IReadOnlyList<ShopItem> Items);

/// <summary>商店靜態資料來源抽象；實作可來自 JSON、DB 或其他 content pipeline。</summary>
public interface IShopCatalog
{
    ShopDefinition? GetShop(int shopId);
    ShopDefinition? GetShopForNpc(int npcId);
    int? GetSellPrice(int itemId);
}

/// <summary>商店交易造成的單一背包格變動。</summary>
public sealed record ShopInventoryMutation(
    InventoryType Type,
    short Slot,
    int ItemId,
    short OldQuantity,
    short NewQuantity)
{
    public bool Removed => NewQuantity <= 0;
}
