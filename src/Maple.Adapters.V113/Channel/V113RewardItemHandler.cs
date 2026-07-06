using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113RewardItemRequest(short Slot, int ItemId);

internal readonly record struct V113ShowExpChairRequest(int ChairId);

internal readonly record struct V113ThrowGrenadeRequest(byte[] Payload);

internal sealed record V113RewardItemResult(
    bool Handled,
    bool CharacterMutated,
    V113RewardItemRequest Request,
    IReadOnlyList<byte[]> SelfPackets,
    IReadOnlyList<byte[]> BroadcastPackets);

internal static class V113RewardItemHandler
{
    private const int DeterministicRewardItemId = 2000000;
    private const short DeterministicRewardQuantity = 1;

    public static V113ShowExpChairRequest ParseShowExpChair(PacketReader reader)
        => new(reader.ReadInt());

    public static V113RewardItemRequest ParseRewardItem(PacketReader reader)
        => new(reader.ReadShort(), reader.ReadInt());

    public static V113RewardItemRequest ParseTreasureChest(PacketReader reader)
        => new(reader.ReadShort(), reader.ReadInt());

    public static V113ThrowGrenadeRequest ParseThrowGrenade(PacketReader reader)
        => new(reader.ReadBytes(reader.Remaining));

    public static V113RewardItemResult HandleRewardItem(PacketReader reader, Player player)
    {
        V113RewardItemRequest request;
        try
        {
            request = ParseRewardItem(reader);
        }
        catch (InvalidDataException)
        {
            return EnableActionsOnly(default);
        }

        // Java delegates reward data to Etc.wz reward entries via MapleItemInformationProvider.getRewardItem.
        // TODO(P003-D4 data): replace this deterministic fallback with WZ-backed StructRewardItem catalog.
        return ConsumeContainerAndGrantReward(
            player,
            request,
            Player.InventoryTypeOf(request.ItemId),
            DeterministicRewardItemId,
            DeterministicRewardQuantity,
            effect: string.Empty);
    }

    public static V113RewardItemResult HandleTreasureChest(PacketReader reader, Player player)
    {
        V113RewardItemRequest request;
        try
        {
            request = ParseTreasureChest(reader);
        }
        catch (InvalidDataException)
        {
            return EnableActionsOnly(default);
        }

        var (keyItemId, rewardItemId, quantity) = request.ItemId switch
        {
            4280000 => (5490000, 1302059, (short)1), // Java gold box reward table first item.
            4280001 => (5490001, 1002452, (short)1), // Java silver box reward table first item.
            _ => (0, 0, (short)0),
        };

        if (keyItemId == 0)
        {
            return EnableActionsOnly(request);
        }

        if (!HasMatchingSource(player, InventoryType.Etc, request) ||
            !HasAny(player, InventoryType.Cash, keyItemId) ||
            !player.CanGainItem(Player.InventoryTypeOf(rewardItemId)))
        {
            return EnableActionsOnly(request);
        }

        if (!TryConsumeFirst(player, InventoryType.Cash, keyItemId, out var keyMutation) || keyMutation is null)
        {
            return EnableActionsOnly(request);
        }

        var result = ConsumeContainerAndGrantReward(
            player,
            request,
            InventoryType.Etc,
            rewardItemId,
            quantity,
            effect: string.Empty,
            extraMutations: [V113ItemUsePackets.ModifyInventoryQuantity(keyMutation)]);

        return result.CharacterMutated
            ? result
            : EnableActionsOnly(request);
    }

    private static V113RewardItemResult ConsumeContainerAndGrantReward(
        Player player,
        V113RewardItemRequest request,
        InventoryType sourceType,
        int rewardItemId,
        short rewardQuantity,
        string effect,
        IReadOnlyList<byte[]>? extraMutations = null)
    {
        if (request.Slot <= 0 ||
            !player.CanGainItem(Player.InventoryTypeOf(rewardItemId)) ||
            !player.TryConsumeInventoryItem(sourceType, request.Slot, request.ItemId, 1, out var sourceMutation) ||
            sourceMutation is null)
        {
            return EnableActionsOnly(request);
        }

        var rewardType = Player.InventoryTypeOf(rewardItemId);
        var rewardItem = player.GainItem(rewardType, rewardItemId, rewardQuantity);
        if (rewardItem is null)
        {
            return EnableActionsOnly(request);
        }

        player.FlushInventory();

        var selfPackets = new List<byte[]>(5)
        {
            V113ItemUsePackets.ModifyInventoryQuantity(sourceMutation),
        };

        if (extraMutations is not null)
        {
            selfPackets.AddRange(extraMutations);
        }

        selfPackets.Add(V113ItemUsePackets.ModifyInventoryAdd(rewardType, rewardItem));
        selfPackets.Add(ShowRewardItemAnimation(rewardItemId, effect));
        selfPackets.Add(V113StatsPackets.EnableActions());

        return new V113RewardItemResult(
            Handled: true,
            CharacterMutated: true,
            request,
            SelfPackets: selfPackets,
            BroadcastPackets: [ShowRewardItemAnimation(rewardItemId, effect, player.Character.Id)]);
    }

    private static bool TryConsumeFirst(
        Player player,
        InventoryType type,
        int itemId,
        out InventoryQuantityMutation? mutation)
    {
        mutation = null;
        var item = player.Inventory.By(type).Items
            .Where(item => item.ItemId == itemId && item.Quantity > 0)
            .OrderBy(item => item.Slot)
            .FirstOrDefault();

        return item is not null &&
               player.TryConsumeInventoryItem(type, item.Slot, itemId, 1, out mutation);
    }

    private static bool HasMatchingSource(Player player, InventoryType type, V113RewardItemRequest request)
    {
        var item = player.Inventory.By(type).Get(request.Slot);
        return item is not null && item.ItemId == request.ItemId && item.Quantity > 0;
    }

    private static bool HasAny(Player player, InventoryType type, int itemId)
        => player.Inventory.By(type).Items.Any(item => item.ItemId == itemId && item.Quantity > 0);

    /// <summary>
    /// Java-source candidate/unverified: MaplePacketCreator.showRewardItemAnimation(itemId, effect).
    /// </summary>
    public static byte[] ShowRewardItemAnimation(int itemId, string? effect)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.ShowItemGainInChat);
        w.WriteByte(0x0B);
        w.WriteInt(itemId);
        WriteEffect(w, effect);
        return w.ToArray();
    }

    /// <summary>
    /// Java-source candidate/unverified: MaplePacketCreator.showRewardItemAnimation(itemId, effect, fromPlayerId).
    /// </summary>
    public static byte[] ShowRewardItemAnimation(int itemId, string? effect, int fromPlayerId)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.ShowForeignEffect);
        w.WriteInt(fromPlayerId);
        w.WriteByte(0x0B);
        w.WriteInt(itemId);
        WriteEffect(w, effect);
        return w.ToArray();
    }

    private static void WriteEffect(PacketWriter w, string? effect)
    {
        if (string.IsNullOrEmpty(effect))
        {
            w.WriteByte(0);
            return;
        }

        w.WriteByte(1);
        w.WriteMapleString(effect);
    }

    private static V113RewardItemResult EnableActionsOnly(V113RewardItemRequest request)
        => new(true, false, request, [V113StatsPackets.EnableActions()], []);
}
