using Maple.Core.Inventory;
using Maple.Core.Shops;
using Maple.Core.World;

namespace Maple.Application.Shops;

public enum ShopTransactionStatus
{
    Success,
    ShopNotOpen,
    ShopNotFound,
    InvalidQuantity,
    ItemNotSold,
    RequiredItemTradeNotSupported,
    NotEnoughMeso,
    InventoryFull,
    ItemMissing,
    SellPriceMissing,
}

public sealed record ShopBuyResult(
    ShopTransactionStatus Status,
    ShopDefinition? Shop = null,
    Item? GainedItem = null,
    int Meso = 0,
    InventoryStackGain? Stack = null);

public sealed record ShopSellResult(
    ShopTransactionStatus Status,
    ShopInventoryMutation? Mutation = null,
    int Meso = 0);

/// <summary>NPC 商店買/賣用例。協定細節留在 Adapters；資料來源透過 IShopCatalog 注入。</summary>
public sealed class ShopService
{
    private readonly IShopCatalog _catalog;

    public ShopService(IShopCatalog catalog)
    {
        _catalog = catalog;
    }

    public ShopDefinition? OpenShop(Player player, int shopOrNpcId)
    {
        var shop = _catalog.GetShop(shopOrNpcId) ?? _catalog.GetShopForNpc(shopOrNpcId);
        if (shop is null)
        {
            return null;
        }

        player.OpenShop(shop.ShopId);
        return shop;
    }

    public ShopBuyResult Buy(Player player, int itemId, short quantity)
    {
        if (quantity <= 0)
        {
            return new ShopBuyResult(ShopTransactionStatus.InvalidQuantity);
        }

        var shop = GetActiveShop(player);
        if (shop is null)
        {
            return new ShopBuyResult(player.ActiveShopId is null ? ShopTransactionStatus.ShopNotOpen : ShopTransactionStatus.ShopNotFound);
        }

        var item = shop.Items.FirstOrDefault(i => i.ItemId == itemId);
        if (item is null || item.Price <= 0)
        {
            return new ShopBuyResult(ShopTransactionStatus.ItemNotSold, shop);
        }

        if (item.RequiredItemId != 0)
        {
            return new ShopBuyResult(ShopTransactionStatus.RequiredItemTradeNotSupported, shop);
        }

        // P181：Java MapleShop.buy——飛鏢／子彈單價即總價、實得 slotMax 個；checkSpace 計入既有堆疊；addById 疊加。
        var rechargeable = Inventory.IsRechargeable(itemId);
        var totalPrice = rechargeable ? item.Price : (long)item.Price * quantity;
        if (totalPrice < 0 || totalPrice > int.MaxValue || player.Character.Meso < totalPrice)
        {
            return new ShopBuyResult(ShopTransactionStatus.NotEnoughMeso, shop);
        }

        if (!player.HasSpaceFor(itemId, quantity))
        {
            return new ShopBuyResult(ShopTransactionStatus.InventoryFull, shop);
        }

        if (rechargeable)
        {
            quantity = player.GetSlotMax(itemId);
        }

        var stack = player.GainItemStacking(itemId, quantity);
        if (!stack.AnyChange)
        {
            return new ShopBuyResult(ShopTransactionStatus.InventoryFull, shop);
        }

        player.GainMeso(-(int)totalPrice);
        player.FlushInventory();
        return new ShopBuyResult(ShopTransactionStatus.Success, shop, stack.Added.FirstOrDefault(), player.Character.Meso, stack);
    }

    public ShopSellResult Sell(Player player, short slot, int itemId, short quantity)
    {
        if (quantity is 0 or -1)
        {
            quantity = 1;
        }

        if (quantity < 0)
        {
            return new ShopSellResult(ShopTransactionStatus.InvalidQuantity);
        }

        if (GetActiveShop(player) is null)
        {
            return new ShopSellResult(player.ActiveShopId is null ? ShopTransactionStatus.ShopNotOpen : ShopTransactionStatus.ShopNotFound);
        }

        var sellPrice = _catalog.GetSellPrice(itemId);
        if (sellPrice is null || sellPrice <= 0)
        {
            return new ShopSellResult(ShopTransactionStatus.SellPriceMissing);
        }

        var type = Player.InventoryTypeOf(itemId);
        if (!player.TryTakeItemFromSlot(type, slot, itemId, quantity, out var mutation) || mutation is null)
        {
            return new ShopSellResult(ShopTransactionStatus.ItemMissing);
        }

        var mesoGainLong = (long)sellPrice.Value * quantity;
        var mesoGain = mesoGainLong > int.MaxValue ? int.MaxValue : (int)mesoGainLong;
        player.GainMeso(mesoGain);
        player.FlushInventory();
        return new ShopSellResult(ShopTransactionStatus.Success, mutation, player.Character.Meso);
    }

    private ShopDefinition? GetActiveShop(Player player)
        => player.ActiveShopId is { } shopId ? _catalog.GetShop(shopId) : null;
}
