using Maple.Core.Inventory;
using Maple.Core.Items;
using Maple.Core.Shops;
using Maple.Core.World;

namespace Maple.Application.Items;

public sealed record UseItemResult(
    bool Success,
    IReadOnlyList<PlayerStatUpdate> StatUpdates,
    ShopInventoryMutation? InventoryMutation)
{
    public static UseItemResult Failed() => new(false, Array.Empty<PlayerStatUpdate>(), null);
}

public sealed class UseItemService
{
    private readonly IItemEffectCatalog _catalog;

    public UseItemService(IItemEffectCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public UseItemResult Use(Player player, short slot, int itemId)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!player.IsAlive)
        {
            return UseItemResult.Failed();
        }

        var item = player.Inventory.By(InventoryType.Use).Get(slot);
        if (item is null || item.ItemId != itemId || item.Quantity < 1)
        {
            return UseItemResult.Failed();
        }

        var effect = _catalog.GetEffect(itemId);
        if (effect is null)
        {
            return UseItemResult.Failed();
        }

        if (!player.TryTakeItemFromSlot(InventoryType.Use, slot, itemId, 1, out var mutation) || mutation is null)
        {
            return UseItemResult.Failed();
        }

        if (mutation.Removed)
        {
            player.Inventory.By(InventoryType.Use).TryTake(slot, out _);
        }

        var updates = ApplyEffect(player, effect);
        player.FlushInventory();
        return new UseItemResult(true, updates, mutation);
    }

    private static IReadOnlyList<PlayerStatUpdate> ApplyEffect(Player player, ItemEffect effect)
    {
        var stats = player.Character.Stats;
        var updates = new List<PlayerStatUpdate>(2);

        var hpRecovery = effect.Hp + (stats.MaxHp * effect.HpRate / 100);
        if (hpRecovery > 0)
        {
            stats.Hp = (short)Math.Min(stats.MaxHp, stats.Hp + hpRecovery);
            updates.Add(new PlayerStatUpdate(PlayerStatKind.Hp, stats.Hp));
        }

        var mpRecovery = effect.Mp + (stats.MaxMp * effect.MpRate / 100);
        if (mpRecovery > 0)
        {
            stats.Mp = (short)Math.Min(stats.MaxMp, stats.Mp + mpRecovery);
            updates.Add(new PlayerStatUpdate(PlayerStatKind.Mp, stats.Mp));
        }

        return updates;
    }
}
