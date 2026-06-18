using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113ItemUnlockRequest(short Slot);

internal sealed record V113ItemUnlockResult(
    bool Handled,
    bool CharacterMutated,
    V113ItemUnlockRequest Request,
    IReadOnlyList<byte[]> Packets);

internal static class V113ItemUnlockHandler
{
    public static V113ItemUnlockRequest Parse(PacketReader reader)
    {
        if (reader.Remaining >= 6)
        {
            _ = reader.ReadShort();    // Java full packet: item count/size
            _ = reader.ReadShort();    // Java full packet: inventory type
            return new V113ItemUnlockRequest(reader.ReadShort());
        }

        if (reader.Remaining >= 2)
        {
            return new V113ItemUnlockRequest(reader.ReadShort());
        }

        throw new InvalidDataException("ITEM_UNLOCK requires a slot.");
    }

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

        if (player.Inventory.By(InventoryType.Equip).Get(request.Slot) is not Equip equip ||
            !ItemFlags.Has(equip.Flag, ItemFlags.Lock))
        {
            return EnableActionsOnly(request);
        }

        equip.Flag = ItemFlags.Clear(equip.Flag, ItemFlags.Lock);
        player.FlushInventory();

        return new V113ItemUnlockResult(
            Handled: true,
            CharacterMutated: true,
            request,
            [
                V113InventoryPackets.ModifyItemUpdate(InventoryType.Equip, request.Slot, equip),
                V113StatsPackets.EnableActions(),
            ]);
    }

    private static V113ItemUnlockResult EnableActionsOnly(V113ItemUnlockRequest request)
        => new(true, false, request, [V113StatsPackets.EnableActions()]);
}
