using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal enum V113InventoryMoveOperation
{
    Ignored,
    Move,
    Equip,
    Unequip,
    Drop,
}

internal readonly record struct V113InventoryMoveResult(
    bool Success,
    V113InventoryMoveOperation Operation,
    ItemMoveRequest Request,
    byte[]? Packet);

internal static class V113InventoryMoveHandler
{
    public static V113InventoryMoveResult ApplyItemMove(PacketReader reader, Player player)
    {
        var req = V113InventoryPackets.ParseItemMove(reader);
        if (!req.IsValidBagType)
            return new(false, V113InventoryMoveOperation.Ignored, req, null);

        if (req.IsWithinBagMove)
            return ApplyBagMove(player, req);

        if (req.IsEquipMove)
            return ApplyEquip(player, req);

        if (req.IsUnequipMove)
            return ApplyUnequip(player, req);

        if (req.IsDropMove)
            return new(false, V113InventoryMoveOperation.Drop, req, null);

        return new(false, V113InventoryMoveOperation.Ignored, req, null);
    }

    private static V113InventoryMoveResult ApplyBagMove(Player player, ItemMoveRequest req)
    {
        if (!player.MoveItem(req.Type, req.Src, req.Dst))
            return new(false, V113InventoryMoveOperation.Move, req, null);

        player.FlushInventory();
        return new(true, V113InventoryMoveOperation.Move, req, V113InventoryPackets.ModifyMove(req.Type, req.Src, req.Dst));
    }

    private static V113InventoryMoveResult ApplyEquip(Player player, ItemMoveRequest req)
    {
        if (!player.Equip(req.Src, req.Dst))
            return new(false, V113InventoryMoveOperation.Equip, req, null);

        player.FlushInventory();
        return new(true, V113InventoryMoveOperation.Equip, req, V113InventoryPackets.ModifyMove(InventoryType.Equip, req.Src, req.Dst));
    }

    private static V113InventoryMoveResult ApplyUnequip(Player player, ItemMoveRequest req)
    {
        if (!player.Unequip(req.Src, req.Dst))
            return new(false, V113InventoryMoveOperation.Unequip, req, null);

        player.FlushInventory();
        return new(true, V113InventoryMoveOperation.Unequip, req, V113InventoryPackets.ModifyMove(InventoryType.Equip, req.Src, req.Dst));
    }
}
