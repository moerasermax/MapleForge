using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113ItemUnlockRequest(short Slot, InventoryType Type = InventoryType.Equip);

internal sealed record V113ItemUnlockResult(
    bool Handled,
    bool CharacterMutated,
    V113ItemUnlockRequest Request,
    IReadOnlyList<byte[]> Packets);

internal static class V113ItemUnlockHandler
{
    /// <summary>封印之鎖解除鑰匙（對照 Java PlayersHandler.UnlockItem 的 UnlockItem 常數）。</summary>
    public const int UnlockKeyItemId = 2051000;

    public static V113ItemUnlockRequest Parse(PacketReader reader)
    {
        if (reader.Remaining >= 6)
        {
            _ = reader.ReadShort();    // Java full packet: item count/size
            var rawType = reader.ReadShort();
            var slot = reader.ReadShort();
            return new V113ItemUnlockRequest(slot, (InventoryType)rawType);
        }

        if (reader.Remaining >= 2)
        {
            return new V113ItemUnlockRequest(reader.ReadShort());
        }

        throw new InvalidDataException("ITEM_UNLOCK requires a slot.");
    }

    /// <summary>
    /// 對照 Java PlayersHandler.UnlockItem：適用任一背包類型（非僅裝備欄），LOCK 優先、
    /// UNTRADEABLE 其次（if/else if，只清一種，不會兩個都清）；清除成功才消耗一顆解鎖鑰匙
    /// （背包沒有鑰匙時 Java 的 removeById 靜默無效果，這裡同樣：找不到就不扣，不擋清除本身）。
    /// </summary>
    public static V113ItemUnlockResult Handle(PacketReader reader, Player player)
    {
        V113ItemUnlockRequest request;
        try
        {
            request = Parse(reader);
        }
        catch (InvalidDataException)
        {
            return EnableActionsOnly(default);
        }

        if (!Enum.IsDefined(request.Type) ||
            player.Inventory.By(request.Type).Get(request.Slot) is not { } item)
        {
            return EnableActionsOnly(request);
        }

        short clearedFlag;
        if (ItemFlags.Has(item.Flag, ItemFlags.Lock))
        {
            clearedFlag = ItemFlags.Lock;
        }
        else if (ItemFlags.Has(item.Flag, ItemFlags.Untradeable))
        {
            clearedFlag = ItemFlags.Untradeable;
        }
        else
        {
            return EnableActionsOnly(request);
        }

        item.Flag = ItemFlags.Clear(item.Flag, clearedFlag);

        var packets = new List<byte[]>
        {
            V113InventoryPackets.ModifyItemUpdate(request.Type, request.Slot, item),
        };

        var keySlot = player.Inventory.By(InventoryType.Use).Items.FirstOrDefault(i => i.ItemId == UnlockKeyItemId)?.Slot;
        if (keySlot is { } slot &&
            player.TryConsumeInventoryItem(InventoryType.Use, slot, UnlockKeyItemId, 1, out var mutation) &&
            mutation is not null)
        {
            packets.Add(V113ItemUsePackets.ModifyInventoryQuantity(mutation));
        }

        player.FlushInventory();
        packets.Add(V113StatsPackets.EnableActions());

        return new V113ItemUnlockResult(true, true, request, packets);
    }

    private static V113ItemUnlockResult EnableActionsOnly(V113ItemUnlockRequest request)
        => new(true, false, request, [V113StatsPackets.EnableActions()]);
}
