using Maple.Core.Inventory;
using Maple.Core.Shops;

namespace Maple.Core.World;

public sealed partial class Player
{
    private int? _activeShopId;

    public int? ActiveShopId => _activeShopId;

    public void OpenShop(int shopId) => _activeShopId = shopId;

    public void CloseShop() => _activeShopId = null;

    public bool CanGainItem(InventoryType type) => Inventory.By(type).FirstFreeSlot() is not null;

    public bool TryTakeItemFromSlot(
        InventoryType type,
        short slot,
        int itemId,
        short quantity,
        out ShopInventoryMutation? mutation)
    {
        mutation = null;
        if (slot <= 0 || quantity <= 0)
        {
            return false;
        }

        var bag = Inventory.By(type);
        var item = bag.Get(slot);
        if (item is null || item.ItemId != itemId)
        {
            return false;
        }

        var oldQuantity = item.IsEquip ? (short)1 : item.Quantity;
        if (quantity > oldQuantity)
        {
            return false;
        }

        var newQuantity = (short)(oldQuantity - quantity);
        item.Quantity = item.IsEquip ? (short)(newQuantity > 0 ? 1 : 0) : newQuantity;
        mutation = new ShopInventoryMutation(type, slot, itemId, oldQuantity, item.Quantity);
        return true;
    }

    public static InventoryType InventoryTypeOf(int itemId)
    {
        var cat = itemId / 1_000_000;
        return cat is >= 1 and <= 5 ? (InventoryType)cat : InventoryType.Etc;
    }
}
