using Maple.Core.CashShop;
using Maple.Core.Inventory;

namespace Maple.Core.World;

public enum BuffItemConsumeStatus
{
    Success,
    InvalidItem,
}

public sealed record BuffItemConsumeResult(
    BuffItemConsumeStatus Status,
    InventoryType Type,
    short Slot,
    int ItemId,
    short RemainingQuantity,
    bool Removed,
    Item? ConsumedItem = null)
{
    public bool Success => Status == BuffItemConsumeStatus.Success;
}

public enum SolomonBookUseStatus
{
    Success,
    InvalidItem,
    GachaponExpAlreadyPending,
    LevelTooHigh,
    NoExperience,
}

public sealed record SolomonBookUseResult(
    SolomonBookUseStatus Status,
    int GachaponExp,
    BuffItemConsumeResult? Consume = null)
{
    public bool Success => Status == SolomonBookUseStatus.Success;
}

public enum GachaponExpClaimStatus
{
    Success,
    NoPendingExp,
}

public sealed record GachaponExpClaimResult(
    GachaponExpClaimStatus Status,
    int ClaimedExperience)
{
    public bool Success => Status == GachaponExpClaimStatus.Success;
}

public enum XmasSurpriseOpenStatus
{
    Success,
    InvalidBox,
    InventoryFull,
}

public sealed record XmasSurpriseOpenResult(
    XmasSurpriseOpenStatus Status,
    Item? Reward = null,
    BuffItemConsumeResult? ConsumedBox = null)
{
    public bool Success => Status == XmasSurpriseOpenStatus.Success;
}

public sealed partial class Player
{
    public BuffItemConsumeResult ConsumeInventoryItem(
        InventoryType type,
        short slot,
        int itemId,
        short quantity = 1)
    {
        if (slot <= 0 || quantity <= 0)
        {
            return InvalidConsume(type, slot, itemId);
        }

        var bag = Inventory.By(type);
        var item = bag.Get(slot);
        if (item is null || item.ItemId != itemId || item.Quantity < quantity)
        {
            return InvalidConsume(type, slot, itemId);
        }

        if (!bag.TryTake(slot, quantity, out var consumed) || consumed is null)
        {
            return InvalidConsume(type, slot, itemId);
        }

        var remaining = bag.Get(slot)?.Quantity ?? 0;
        return new BuffItemConsumeResult(
            BuffItemConsumeStatus.Success,
            type,
            slot,
            itemId,
            remaining,
            Removed: remaining == 0,
            ConsumedItem: consumed);
    }

    public SolomonBookUseResult UseSolomonBook(short slot, int itemId, int experience)
    {
        if (experience <= 0)
        {
            return new SolomonBookUseResult(SolomonBookUseStatus.NoExperience, Character.GachExp);
        }

        var item = Inventory.By(InventoryType.Use).Get(slot);
        if (item is null || item.ItemId != itemId || item.Quantity <= 0)
        {
            return new SolomonBookUseResult(SolomonBookUseStatus.InvalidItem, Character.GachExp);
        }

        if (Character.GachExp > 0)
        {
            return new SolomonBookUseResult(SolomonBookUseStatus.GachaponExpAlreadyPending, Character.GachExp);
        }

        if (Character.Level > 50)
        {
            return new SolomonBookUseResult(SolomonBookUseStatus.LevelTooHigh, Character.GachExp);
        }

        var consumed = ConsumeInventoryItem(InventoryType.Use, slot, itemId);
        if (!consumed.Success)
        {
            return new SolomonBookUseResult(SolomonBookUseStatus.InvalidItem, Character.GachExp);
        }

        Character.GachExp = (int)Math.Min(int.MaxValue, (long)Character.GachExp + experience);
        return new SolomonBookUseResult(SolomonBookUseStatus.Success, Character.GachExp, consumed);
    }

    public GachaponExpClaimResult ClaimGachaponExperience()
    {
        if (Character.GachExp <= 0)
        {
            return new GachaponExpClaimResult(GachaponExpClaimStatus.NoPendingExp, 0);
        }

        var claimed = Character.GachExp;
        Character.GachExp = 0;
        return new GachaponExpClaimResult(GachaponExpClaimStatus.Success, claimed);
    }

    public XmasSurpriseOpenResult OpenXmasSurpriseBox(
        long cashId,
        int boxItemId,
        CashItemDefinition reward,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reward);

        var cashBag = Inventory.By(InventoryType.Cash);
        var box = cashBag.Items.FirstOrDefault(i =>
            i.UniqueId == cashId &&
            i.ItemId == boxItemId &&
            i.Quantity > 0);
        if (box is null)
        {
            return new XmasSurpriseOpenResult(XmasSurpriseOpenStatus.InvalidBox);
        }

        var gained = GainCashShopItem(reward, now);
        if (gained is null)
        {
            return new XmasSurpriseOpenResult(XmasSurpriseOpenStatus.InventoryFull);
        }

        var consumed = ConsumeInventoryItem(InventoryType.Cash, box.Slot, boxItemId);
        return consumed.Success
            ? new XmasSurpriseOpenResult(XmasSurpriseOpenStatus.Success, gained, consumed)
            : new XmasSurpriseOpenResult(XmasSurpriseOpenStatus.InvalidBox);
    }

    private static BuffItemConsumeResult InvalidConsume(InventoryType type, short slot, int itemId)
        => new(BuffItemConsumeStatus.InvalidItem, type, slot, itemId, 0, Removed: false);
}
