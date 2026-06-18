using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113UseSkillBookRequest(int Tick, short Slot, int ItemId);

internal sealed record V113SkillBookHandleResult(
    bool Handled,
    bool CharacterMutated,
    bool SendEnableActions,
    bool CanUse,
    bool Success,
    int SkillId,
    int MasterLevel,
    IReadOnlyList<byte[]> SelfPackets,
    byte[]? BroadcastPacket);

internal static class V113SkillBookPackets
{
    public const short RecvUseSkillBook = 0x4C;
    public const short SendUseSkillBook = 0x31;

    public static V113UseSkillBookRequest ParseUseSkillBook(PacketReader reader)
    {
        var tick = reader.ReadInt();
        var slot = reader.ReadShort();
        var itemId = reader.ReadInt();
        return new V113UseSkillBookRequest(tick, slot, itemId);
    }

    public static byte[] UseSkillBook(
        int characterId,
        int skillId,
        int masterLevel,
        bool canUse,
        bool success)
    {
        var w = new PacketWriter(18);
        w.WriteShort(V113ChannelSendOp.UseSkillBook);
        w.WriteInt(characterId);
        w.WriteByte(1);
        w.WriteInt(skillId);
        w.WriteInt(masterLevel);
        w.WriteByte(canUse ? (byte)1 : (byte)0);
        w.WriteByte(success ? (byte)1 : (byte)0);
        return w.ToArray();
    }
}

internal static class V113SkillBookHandler
{
    public static V113SkillBookHandleResult HandleUseSkillBook(
        PacketReader reader,
        Player player,
        ISkillBookCatalog catalog)
    {
        var request = V113SkillBookPackets.ParseUseSkillBook(reader);
        var useInventory = player.Inventory.By(InventoryType.Use);
        var item = useInventory.Get(request.Slot);

        if (item is null || item.ItemId != request.ItemId || item.Quantity < 1)
        {
            return EnableOnly();
        }

        if (!IsSkillBookItem(request.ItemId))
        {
            return EnableOnly();
        }

        var definition = catalog.GetByItemId(request.ItemId);
        if (definition is null)
        {
            return EnableOnly();
        }

        var skillId = definition.SkillIds.FirstOrDefault(skill => skill > 0 && skill / 10000 == player.Character.Job);
        if (skillId <= 0)
        {
            return Broadcast(player, canUse: false, success: false, skillId: 0, masterLevel: 0);
        }

        var currentLevel = player.GetSkillLevel(skillId);
        var currentMasterLevel = player.GetMasterLevel(skillId);
        var canUse = currentLevel >= definition.ReqSkillLevel && currentMasterLevel < definition.MasterLevel;
        if (!canUse)
        {
            return Broadcast(player, canUse: false, success: false, skillId, definition.MasterLevel);
        }

        var success = definition.SuccessRate > 0 && Random.Shared.Next(100) < definition.SuccessRate;
        var oldQuantity = item.Quantity;
        _ = useInventory.TryTake(request.Slot, 1, out _);

        if (success)
        {
            player.ChangeSkillLevel(
                skillId,
                ClampToByte(currentLevel),
                ClampToByte(definition.MasterLevel));
        }

        player.FlushInventory();

        var selfPackets = new List<byte[]>(2)
        {
            V113ItemUsePackets.ModifyInventoryQuantity(
                new InventoryQuantityMutation(
                    InventoryType.Use,
                    request.Slot,
                    request.ItemId,
                    oldQuantity,
                    (short)(oldQuantity - 1))),
        };

        if (success)
        {
            selfPackets.Add(V113StatsPackets.UpdateSkill(skillId, currentLevel, definition.MasterLevel));
        }

        return new V113SkillBookHandleResult(
            Handled: true,
            CharacterMutated: true,
            SendEnableActions: true,
            CanUse: true,
            Success: success,
            SkillId: skillId,
            MasterLevel: definition.MasterLevel,
            SelfPackets: selfPackets,
            BroadcastPacket: V113SkillBookPackets.UseSkillBook(
                player.Character.Id,
                skillId,
                definition.MasterLevel,
                canUse: true,
                success));
    }

    private static V113SkillBookHandleResult EnableOnly()
        => new(
            Handled: true,
            CharacterMutated: false,
            SendEnableActions: true,
            CanUse: false,
            Success: false,
            SkillId: 0,
            MasterLevel: 0,
            SelfPackets: Array.Empty<byte[]>(),
            BroadcastPacket: null);

    private static V113SkillBookHandleResult Broadcast(
        Player player,
        bool canUse,
        bool success,
        int skillId,
        int masterLevel)
        => new(
            Handled: true,
            CharacterMutated: false,
            SendEnableActions: true,
            CanUse: canUse,
            Success: success,
            SkillId: skillId,
            MasterLevel: masterLevel,
            SelfPackets: Array.Empty<byte[]>(),
            BroadcastPacket: V113SkillBookPackets.UseSkillBook(
                player.Character.Id,
                skillId,
                masterLevel,
                canUse,
                success));

    private static bool IsSkillBookItem(int itemId) => itemId / 10000 is 228 or 229;

    private static byte ClampToByte(int value) => (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
}
