using Maple.Application.Items;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal sealed record V113UseConsumableResult(
    bool Handled,
    bool CharacterMutated,
    IReadOnlyList<byte[]> Packets);

public sealed class V113UseConsumableHandler
{
    private readonly UseItemService _service;

    public V113UseConsumableHandler(UseItemService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    internal V113UseConsumableResult Handle(PacketReader reader, Player player)
    {
        if (reader.Remaining < 10)
        {
            return EnableActionsOnly();
        }

        reader.Skip(4);
        var slot = reader.ReadShort();
        var itemId = reader.ReadInt();

        var result = _service.Use(player, slot, itemId);
        if (!result.Success || result.InventoryMutation is null)
        {
            return EnableActionsOnly();
        }

        var packets = new List<byte[]>
        {
            V113ShopPackets.ModifyInventoryQuantity(result.InventoryMutation),
        };

        if (result.StatUpdates.Count > 0)
        {
            packets.Add(V113StatsPackets.UpdateStats(result.StatUpdates));
        }

        packets.Add(V113StatsPackets.EnableActions());
        return new V113UseConsumableResult(true, true, packets);
    }

    private static V113UseConsumableResult EnableActionsOnly()
        => new(true, false, [V113StatsPackets.EnableActions()]);
}
