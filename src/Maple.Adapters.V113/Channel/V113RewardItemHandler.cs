using Maple.Application.Items;
using Maple.Core.Inventory;
using Maple.Core.Items;
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
    IReadOnlyList<byte[]> BroadcastPackets)
{
    /// <summary>
    /// 對照 Java <c>World.Broadcast.broadcastMessage(getGachaponMega(...))</c>：寶箱抽到稀有道具時
    /// 的全服公告，不限地圖（見 <see cref="V113BroadcastPackets.GachaponMega"/>）。
    /// </summary>
    public IReadOnlyList<byte[]> ChannelBroadcastPackets { get; init; } = Array.Empty<byte[]>();
}

internal static class V113RewardItemHandler
{
    private const short DefaultRewardQuantity = 1;

    public static V113ShowExpChairRequest ParseShowExpChair(PacketReader reader)
        => new(reader.ReadInt());

    public static V113RewardItemRequest ParseRewardItem(PacketReader reader)
        => new(reader.ReadShort(), reader.ReadInt());

    public static V113RewardItemRequest ParseTreasureChest(PacketReader reader)
        => new(reader.ReadShort(), reader.ReadInt());

    public static V113ThrowGrenadeRequest ParseThrowGrenade(PacketReader reader)
        => new(reader.ReadBytes(reader.Remaining));

    internal const string RewardInventoryFullMessage = "你有一個欄位滿了 請空出來再打開";
    internal const string RewardUnknownErrorMessage = "未知的錯誤.";

    /// <summary>
    /// P180：對照 Java <c>InventoryHandler.UseRewardItem</c>：先送 enableActions；來源道具相符時，裝備／消耗／裝飾／其他欄
    /// 都要有空位（否則黃字提示）；有獎勵表（totalprob &gt; 0）則抽獎——裝備帶 period（Java <c>period * 60 * 60 * 10</c> 毫秒）、
    /// 其餘給 count 個——移除 1 個來源、自己與地圖他人看獎勵動畫；無獎勵表提示「未知的錯誤.」。
    /// </summary>
    public static V113RewardItemResult HandleRewardItem(
        PacketReader reader,
        Player player,
        IItemUseCatalog? catalog,
        Random random,
        DateTimeOffset now)
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

        var sourceType = Player.InventoryTypeOf(request.ItemId);
        if (!HasMatchingSource(player, sourceType, request))
        {
            return EnableActionsOnly(request);
        }

        if (!player.CanGainItem(InventoryType.Equip) || !player.CanGainItem(InventoryType.Use)
            || !player.CanGainItem(InventoryType.Setup) || !player.CanGainItem(InventoryType.Etc))
        {
            return Notice(request, RewardInventoryFullMessage);
        }

        if (catalog?.GetRewardItems(request.ItemId) is not { TotalProb: > 0 } table || table.Roll(random) is not { } reward)
        {
            return Notice(request, RewardUnknownErrorMessage);
        }

        if (!player.TryConsumeInventoryItem(sourceType, request.Slot, request.ItemId, 1, out var sourceMutation) || sourceMutation is null)
        {
            return EnableActionsOnly(request);
        }

        var rewardType = Player.InventoryTypeOf(reward.ItemId);
        var rewardItem = player.GainItem(rewardType, reward.ItemId, Math.Max((short)1, reward.Quantity));
        if (rewardItem is null)
        {
            player.FlushInventory();
            return new V113RewardItemResult(true, true, request, [V113StatsPackets.EnableActions(), V113ItemUsePackets.ModifyInventoryQuantity(sourceMutation)], []);
        }

        if (rewardType == InventoryType.Equip && reward.Period > 0)
        {
            rewardItem.Expiration = now.ToUnixTimeMilliseconds() + (reward.Period * 60L * 60 * 10);
        }

        player.FlushInventory();
        return new V113RewardItemResult(
            Handled: true,
            CharacterMutated: true,
            request,
            SelfPackets:
            [
                V113StatsPackets.EnableActions(),
                V113ItemUsePackets.ModifyInventoryQuantity(sourceMutation),
                V113ItemUsePackets.ModifyInventoryAdd(rewardType, rewardItem),
                ShowRewardItemAnimation(reward.ItemId, reward.Effect),
            ],
            BroadcastPackets: [ShowRewardItemAnimation(reward.ItemId, reward.Effect, player.Character.Id)]);
    }

    private static V113RewardItemResult Notice(V113RewardItemRequest request, string message)
        => new(true, false, request, [V113StatsPackets.EnableActions(), V113BroadcastPackets.ServerNotice(6, message)], []);

    private const int SpecialPotionItemId = 2000004;
    private const int SuperPotionItemId = 2000005;
    private const short SpecialPotionQuantity = 200;
    private const short SuperPotionQuantity = 100;

    public static V113RewardItemResult HandleTreasureChest(PacketReader reader, Player player, RandomRewardsCatalog randomRewards, int channel)
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

        // 對照 Java InventoryHandler.UseTreasureChest：金/銀寶箱各自查 RandomRewards 加權表抽獎；
        // 特殊/超級藥水額外給固定數量（200/100），其餘一律 1 個。
        var (keyItemId, rewardItemId, boxName) = request.ItemId switch
        {
            4280000 => (5490000, randomRewards.GetGoldBoxReward(), "金寶箱"),
            4280001 => (5490001, randomRewards.GetSilverBoxReward(), "銀寶箱"),
            _ => (0, 0, string.Empty),
        };

        if (keyItemId == 0)
        {
            return EnableActionsOnly(request);
        }

        var quantity = rewardItemId switch
        {
            SpecialPotionItemId => SpecialPotionQuantity,
            SuperPotionItemId => SuperPotionQuantity,
            _ => DefaultRewardQuantity,
        };

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
            extraMutations: [V113ItemUsePackets.ModifyInventoryQuantity(keyMutation)],
            gachapon: new GachaponContext(channel, boxName));

        return result.CharacterMutated
            ? result
            : EnableActionsOnly(request);
    }

    /// <summary>對照 Java <c>InventoryHandler.UseTreasureChest</c> 稀有度全服廣播所需的上下文。</summary>
    private readonly record struct GachaponContext(int Channel, string BoxName);

    private static V113RewardItemResult ConsumeContainerAndGrantReward(
        Player player,
        V113RewardItemRequest request,
        InventoryType sourceType,
        int rewardItemId,
        short rewardQuantity,
        string effect,
        IReadOnlyList<byte[]>? extraMutations = null,
        GachaponContext? gachapon = null)
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

        var channelBroadcasts = Array.Empty<byte[]>();
        if (gachapon is { } ctx && RandomRewardsCatalog.GetGachaponRareness(rewardItemId) > 0)
        {
            var message = $"\t恭喜 {player.Character.Name} : 從{ctx.BoxName}抽到！";
            channelBroadcasts = [V113BroadcastPackets.GachaponMega(message, ctx.Channel, rewardItem)];
        }

        return new V113RewardItemResult(
            Handled: true,
            CharacterMutated: true,
            request,
            SelfPackets: selfPackets,
            BroadcastPackets: [ShowRewardItemAnimation(rewardItemId, effect, player.Character.Id)])
        {
            ChannelBroadcastPackets = channelBroadcasts,
        };
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
