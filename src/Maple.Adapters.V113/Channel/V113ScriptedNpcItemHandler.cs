using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113ScriptedNpcItemRequest(int Tick, short Slot, int ItemId);

internal sealed record V113ScriptedNpcItemResult(
    bool Handled,
    bool CharacterMutated,
    V113ScriptedNpcItemRequest Request,
    IReadOnlyList<byte[]> Packets);

internal static class V113ScriptedNpcItemHandler
{
    public static V113ScriptedNpcItemRequest Parse(PacketReader reader)
    {
        if (reader.Remaining >= 10)
        {
            var tick = reader.ReadInt();
            var slot = reader.ReadShort();
            var itemId = reader.ReadInt();
            return new V113ScriptedNpcItemRequest(tick, slot, itemId);
        }

        if (reader.Remaining >= 6)
        {
            var slot = reader.ReadShort();
            var itemId = reader.ReadInt();
            return new V113ScriptedNpcItemRequest(0, slot, itemId);
        }

        throw new InvalidDataException("USE_SCRIPTED_NPC_ITEM requires slot and itemId.");
    }

    public static V113ScriptedNpcItemResult Handle(PacketReader reader, Player player)
    {
        V113ScriptedNpcItemRequest request;
        try
        {
            request = Parse(reader);
        }
        catch (InvalidDataException)
        {
            return EnableActionsOnly(default);
        }

        if (!player.TryConsumeInventoryItem(
                InventoryType.Use,
                request.Slot,
                request.ItemId,
                1,
                out var mutation) ||
            mutation is null)
        {
            return EnableActionsOnly(request);
        }

        player.FlushInventory();
        return new V113ScriptedNpcItemResult(
            Handled: true,
            CharacterMutated: true,
            request,
            [
                V113ItemUsePackets.ModifyInventoryQuantity(mutation),
                V113StatsPackets.EnableActions(),
            ]);
    }

    private static V113ScriptedNpcItemResult EnableActionsOnly(V113ScriptedNpcItemRequest request)
        => new(true, false, request, [V113StatsPackets.EnableActions()]);
}
