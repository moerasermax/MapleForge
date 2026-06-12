using Maple.Application.NpcItemServices;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.NpcItemServices;

namespace Maple.Adapters.V113.Channel;

internal static class V113RepairPackets
{
    public const short SendRepairWindow = unchecked((short)0xD5);

    // Java recv.properties comments these out; effective RecvPacketOpcode value is -2 until central routing confirms live values.
    public const short CommentedRecvRepairAll = 0x72;
    public const short CommentedRecvRepair = 0x73;
    public const short EffectiveUnmappedRecvValue = unchecked((short)0xFFFE);

    public static int ParseRepairPosition(PacketReader reader)
    {
        if (reader.Remaining < 4)
        {
            throw new InvalidDataException("REPAIR requires int position.");
        }

        return reader.ReadInt();
    }

    public static byte[] RepairWindow(int npcId)
    {
        var w = new PacketWriter(10);
        w.WriteShort(SendRepairWindow);
        w.WriteInt(0x22);
        w.WriteInt(npcId);
        return w.ToArray();
    }

    public static byte[] UpdateMeso(int meso)
        => V113ShopPackets.UpdateMeso(meso, itemReaction: false);

    public static byte[] ModifyInventoryRepair(EquipRepairMutation mutation)
    {
        var w = new PacketWriter(10);
        w.WriteShort(V113ChannelSendOp.ModifyInventoryItem);
        w.WriteByte(0);
        w.WriteByte(1);
        w.WriteByte(1);
        w.WriteByte((byte)InventoryType.Equip);
        w.WriteShort(mutation.Position);
        w.WriteShort(1);
        return w.ToArray();
    }
}

internal sealed record V113RepairHandleResult(
    bool Handled,
    bool CharacterMutated,
    EquipRepairResult Repair,
    IReadOnlyList<byte[]> Packets);

public sealed class V113RepairHandler
{
    private readonly EquipRepairService _repairs;

    public V113RepairHandler(EquipRepairService repairs)
    {
        _repairs = repairs;
    }

    internal V113RepairHandleResult HandleRepair(PacketReader reader, Maple.Core.World.Player player)
    {
        EquipRepairResult result;
        try
        {
            result = _repairs.Repair(player, V113RepairPackets.ParseRepairPosition(reader));
        }
        catch (InvalidDataException)
        {
            result = EquipRepairResult.Failed(EquipRepairStatus.InvalidPosition, player.Character.Meso);
        }

        return ToHandleResult(result);
    }

    internal V113RepairHandleResult HandleRepairAll(Maple.Core.World.Player player)
        => ToHandleResult(_repairs.RepairAll(player));

    private static V113RepairHandleResult ToHandleResult(EquipRepairResult result)
    {
        if (!result.Applied)
        {
            return new V113RepairHandleResult(false, false, result, Array.Empty<byte[]>());
        }

        var packets = new List<byte[]>(1 + result.Mutations.Count)
        {
            V113RepairPackets.UpdateMeso(result.Meso),
        };
        foreach (var mutation in result.Mutations)
        {
            packets.Add(V113RepairPackets.ModifyInventoryRepair(mutation));
        }

        return new V113RepairHandleResult(true, true, result, packets);
    }
}
