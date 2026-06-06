using Maple.Core.CashShop;
using Maple.Core.Inventory;

namespace Maple.Core.World;

public sealed partial class Player
{
    private long _nextCashShopUniqueId = 1;

    public Item? GainCashShopItem(CashItemDefinition cashItem, DateTimeOffset? now = null)
    {
        var type = InventoryTypeOf(cashItem.ItemId);
        var item = CreateCashShopItem(cashItem, type, now ?? DateTimeOffset.UtcNow);
        var gained = Inventory.By(type).Gain(item);
        if (gained is null)
        {
            return null;
        }

        return gained;
    }

    private Item CreateCashShopItem(CashItemDefinition cashItem, InventoryType type, DateTimeOffset now)
    {
        var expiration = CalculateCashShopExpiration(cashItem, now);
        var uniqueId = NextCashShopUniqueId();

        if (type == InventoryType.Equip)
        {
            return new Equip
            {
                ItemId = cashItem.ItemId,
                Quantity = 1,
                Expiration = expiration,
                UniqueId = uniqueId,
            };
        }

        return new Item
        {
            ItemId = cashItem.ItemId,
            Quantity = cashItem.Quantity,
            Expiration = expiration,
            UniqueId = uniqueId,
        };
    }

    private long NextCashShopUniqueId()
    {
        var maxExisting = Inventory
            .Flush()
            .Select(static i => i.UniqueId)
            .DefaultIfEmpty(0)
            .Max();

        if (_nextCashShopUniqueId <= maxExisting)
        {
            _nextCashShopUniqueId = maxExisting + 1;
        }

        return _nextCashShopUniqueId++;
    }

    private static long CalculateCashShopExpiration(CashItemDefinition cashItem, DateTimeOffset now)
    {
        var days = cashItem.PeriodDays <= 0 ? 45 : cashItem.PeriodDays;
        return now.AddDays(days).ToUnixTimeMilliseconds();
    }
}
