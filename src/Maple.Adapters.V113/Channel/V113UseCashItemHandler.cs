using Maple.Application.NpcItemServices;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Shops;
using Maple.Core.World;
using Microsoft.Extensions.Logging;

namespace Maple.Adapters.V113.Channel;

/// <summary>Result of handling USE_CASH_ITEM (0x49).</summary>
internal sealed record V113UseCashItemResult(
    bool Handled,
    bool CharacterMutated,
    IReadOnlyList<byte[]> Packets);

/// <summary>
/// v113 USE_CASH_ITEM (0x49) handler. Cash items used from the Cash inventory tab
/// dispatch through this opcode with a switch on itemId.
/// Currently routes 5230000 (Owl of Minerva cash) to <see cref="OwlService"/>.
/// </summary>
public sealed class V113UseCashItemHandler
{
    private readonly OwlService _owlService;
    private readonly ILogger<V113UseCashItemHandler> _log;

    public V113UseCashItemHandler(OwlService owlService, ILogger<V113UseCashItemHandler> log)
    {
        _owlService = owlService;
        _log = log;
    }

    internal V113UseCashItemResult Handle(PacketReader reader, Player player)
    {
        if (reader.Remaining < 6)
        {
            return EnableActionsOnly();
        }

        short slot = reader.ReadShort();
        int itemId = reader.ReadInt();

        // Validate: player has the item in Cash inventory at that slot with matching itemId
        var cashBag = player.Inventory.By(InventoryType.Cash);
        var item = cashBag.Get(slot);
        if (item is null || item.ItemId != itemId || item.Quantity <= 0)
        {
            _log.LogDebug("[UseCashItem] Item validation failed slot={Slot} itemId={ItemId}", slot, itemId);
            return EnableActionsOnly();
        }

        return itemId switch
        {
            OwlService.CashOwlItemId => HandleOwlOfMinerva(reader, player, slot, itemId),
            _ => HandleUnknown(itemId),
        };
    }

    private V113UseCashItemResult HandleOwlOfMinerva(PacketReader reader, Player player, short slot, int itemId)
    {
        if (reader.Remaining < 4)
        {
            return EnableActionsOnly();
        }

        int searchItemId = reader.ReadInt();

        var searchResult = _owlService.Search(player, searchItemId);
        if (!searchResult.Success || searchResult.Entries.Count == 0)
        {
            return EnableActionsOnly();
        }

        var packets = new List<byte[]>();

        // Java order: send search results first, then consume
        packets.Add(V113OwlPackets.OwlSearched(searchItemId, searchResult.Entries));

        bool consumed = player.TryTakeItemFromSlot(InventoryType.Cash, slot, itemId, 1, out var mutation);
        if (consumed)
        {
            player.FlushInventory();
            packets.Add(V113ShopPackets.ModifyInventoryQuantity(mutation!));
        }

        packets.Add(V113StatsPackets.EnableActions());

        return new V113UseCashItemResult(true, consumed, packets);
    }

    private V113UseCashItemResult HandleUnknown(int itemId)
    {
        _log.LogDebug("[UseCashItem] Unhandled cash item {ItemId}", itemId);
        return EnableActionsOnly();
    }

    private static V113UseCashItemResult EnableActionsOnly()
        => new(true, false, [V113StatsPackets.EnableActions()]);
}
