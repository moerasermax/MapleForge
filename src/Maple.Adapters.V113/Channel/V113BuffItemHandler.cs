using Maple.Application.Stats;
using Maple.Core.Accounts;
using Maple.Core.CashShop;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Skills;
using Maple.Core.World;
using System.Security.Cryptography;

namespace Maple.Adapters.V113.Channel;

public interface IV113XmasSurpriseRewardSource
{
    int NextSerialNumber();
}

public sealed class V113XmasSurpriseRewardSource : IV113XmasSurpriseRewardSource
{
    public int NextSerialNumber()
        => RandomNumberGenerator.GetInt32(3) switch
        {
            0 => 20300223,
            1 => 20300221,
            _ => 20300275,
        };
}

public sealed record V113BuffItemHandleResult(
    bool Handled,
    bool CharacterMutated,
    IReadOnlyList<byte[]> Packets);

public sealed record V113TransformPlayerHandleResult(
    bool Handled,
    bool SourceCharacterMutated,
    bool TargetRuntimeMutated,
    Player? Target,
    IReadOnlyList<byte[]> SourcePackets,
    IReadOnlyList<byte[]> TargetPackets,
    IReadOnlyList<byte[]> BroadcastPackets);

public sealed class V113BuffItemHandler
{
    private readonly StatsService _statsService;
    private readonly ICashItemCatalog _cashItems;
    private readonly IV113XmasSurpriseRewardSource _xmasRewards;
    private readonly TimeProvider _timeProvider;

    public V113BuffItemHandler(
        StatsService statsService,
        ICashItemCatalog cashItems,
        IV113XmasSurpriseRewardSource xmasRewards,
        TimeProvider? timeProvider = null)
    {
        _statsService = statsService;
        _cashItems = cashItems;
        _xmasRewards = xmasRewards;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public V113BuffItemHandleResult HandleSolomon(PacketReader reader, Player player)
    {
        V113SolomonRequest request;
        try
        {
            request = V113BuffItemPackets.ParseSolomon(reader);
        }
        catch (InvalidDataException)
        {
            return NotHandled();
        }

        _ = request.Tick;
        var packets = new List<byte[]> { V113StatsPackets.EnableActions() };
        var experience = V113BuffItemPackets.GetSolomonExperience(request.ItemId);
        var result = player.UseSolomonBook(request.Slot, request.ItemId, experience);
        if (!result.Success)
        {
            return new V113BuffItemHandleResult(true, false, packets);
        }

        if (result.Consume is { } consume)
        {
            packets.Add(V113BuffItemPackets.ModifyInventoryQuantity(
                consume.Type,
                consume.Slot,
                consume.RemainingQuantity,
                consume.Removed));
        }

        packets.Add(V113StatsPackets.UpdateStats(new[]
        {
            new PlayerStatUpdate(PlayerStatKind.GachaponExp, result.GachaponExp),
        }));
        player.FlushInventory();
        return new V113BuffItemHandleResult(true, true, packets);
    }

    public V113BuffItemHandleResult HandleGachExp(PacketReader reader, Player player)
    {
        try
        {
            _ = V113BuffItemPackets.ParseGachExp(reader);
        }
        catch (InvalidDataException)
        {
            return NotHandled();
        }

        var packets = new List<byte[]> { V113StatsPackets.EnableActions() };
        var claimed = player.ClaimGachaponExperience();
        if (!claimed.Success)
        {
            return new V113BuffItemHandleResult(true, false, packets);
        }

        var mutation = _statsService.GainExperience(player, claimed.ClaimedExperience);
        if (V113StatsHandlers.EncodeUpdateStats(mutation) is { } statsPacket)
        {
            packets.Add(statsPacket);
        }

        packets.Add(V113BuffItemPackets.GainExpOthers(claimed.ClaimedExperience, inChat: true, white: false));
        packets.Add(V113StatsPackets.UpdateStats(new[]
        {
            new PlayerStatUpdate(PlayerStatKind.GachaponExp, 0),
        }));
        return new V113BuffItemHandleResult(true, true, packets);
    }

    public V113TransformPlayerHandleResult HandleTransformPlayer(
        PacketReader reader,
        Player source,
        IEnumerable<Player> mapPlayers)
    {
        V113TransformPlayerRequest request;
        try
        {
            request = V113BuffItemPackets.ParseTransformPlayer(reader);
        }
        catch (InvalidDataException)
        {
            return TransformNotHandled();
        }

        _ = request.Tick;
        var effect = V113BuffItemPackets.GetTransformEffect(request.ItemId);
        if (effect is null || !HasUseItemInSlot(source, request.Slot, request.ItemId))
        {
            return TransformHandled(
                sourcePackets: new[] { V113StatsPackets.EnableActions() },
                sourceCharacterMutated: false);
        }

        var target = mapPlayers.FirstOrDefault(p =>
            string.Equals(p.Character.Name, request.TargetName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return TransformHandled();
        }

        var now = _timeProvider.GetUtcNow();
        var applied = target.ApplySkillEffect(effect, now);
        if (applied.Status != PlayerSkillApplicationStatus.Applied || applied.Buff is null)
        {
            return TransformHandled(
                sourcePackets: new[] { V113StatsPackets.EnableActions() },
                sourceCharacterMutated: false,
                target: target);
        }

        var consumed = source.ConsumeInventoryItem(InventoryType.Use, request.Slot, request.ItemId);
        if (!consumed.Success)
        {
            return TransformHandled(
                sourcePackets: new[] { V113StatsPackets.EnableActions() },
                sourceCharacterMutated: false,
                target: target,
                targetRuntimeMutated: true);
        }

        source.FlushInventory();
        return TransformHandled(
            sourcePackets: new[]
            {
                V113BuffItemPackets.ModifyInventoryQuantity(
                    consumed.Type,
                    consumed.Slot,
                    consumed.RemainingQuantity,
                    consumed.Removed),
            },
            targetPackets: new[]
            {
                V113SkillPackets.GiveBuff(
                    applied.Buff.SourceId,
                    applied.Buff.DurationMilliseconds,
                    applied.Buff.Stats,
                    effect),
            },
            broadcastPackets: new[]
            {
                V113BuffItemPackets.GiveForeignBuff(
                    target.Character.Id,
                    applied.Buff.Stats,
                    isMorph: true),
            },
            sourceCharacterMutated: true,
            targetRuntimeMutated: true,
            target: target);
    }

    public V113BuffItemHandleResult HandleXmasSurprise(PacketReader reader, Account account, Player player)
    {
        V113XmasSurpriseRequest request;
        try
        {
            request = V113BuffItemPackets.ParseXmasSurprise(reader);
        }
        catch (InvalidDataException)
        {
            return NotHandled();
        }

        var rewardSerialNumber = _xmasRewards.NextSerialNumber();
        var reward = _cashItems.GetBySerialNumber(rewardSerialNumber);
        if (reward is null || V113BuffItemPackets.IsCashBlocked(reward.ItemId))
        {
            return new V113BuffItemHandleResult(
                true,
                false,
                new[] { V113CashShopPackets.SendCashShopFail(0) });
        }

        var opened = player.OpenXmasSurpriseBox(
            request.CashId,
            V113BuffItemPackets.XmasSurpriseBoxItemId,
            reward,
            _timeProvider.GetUtcNow());
        return opened.Status switch
        {
            XmasSurpriseOpenStatus.Success when opened.Reward is not null => XmasSuccess(
                request.CashId,
                opened.Reward,
                account.Id,
                player),
            XmasSurpriseOpenStatus.InventoryFull => new V113BuffItemHandleResult(
                true,
                false,
                new[] { V113BuffItemPackets.ShowXmasSurprise(true, request.CashId, null, account.Id) }),
            _ => new V113BuffItemHandleResult(true, false, Array.Empty<byte[]>()),
        };
    }

    private static bool HasUseItemInSlot(Player player, short slot, int itemId)
    {
        var item = player.Inventory.By(InventoryType.Use).Get(slot);
        return item is not null && item.ItemId == itemId && item.Quantity > 0;
    }

    private static V113BuffItemHandleResult XmasSuccess(long cashId, Item reward, int accountId, Player player)
    {
        player.FlushInventory();
        return new V113BuffItemHandleResult(
            true,
            true,
            new[] { V113BuffItemPackets.ShowXmasSurprise(false, cashId, reward, accountId) });
    }

    private static V113BuffItemHandleResult NotHandled()
        => new(false, false, Array.Empty<byte[]>());

    private static V113TransformPlayerHandleResult TransformNotHandled()
        => new(
            false,
            false,
            false,
            null,
            Array.Empty<byte[]>(),
            Array.Empty<byte[]>(),
            Array.Empty<byte[]>());

    private static V113TransformPlayerHandleResult TransformHandled(
        IReadOnlyList<byte[]>? sourcePackets = null,
        IReadOnlyList<byte[]>? targetPackets = null,
        IReadOnlyList<byte[]>? broadcastPackets = null,
        bool sourceCharacterMutated = false,
        bool targetRuntimeMutated = false,
        Player? target = null)
        => new(
            true,
            sourceCharacterMutated,
            targetRuntimeMutated,
            target,
            sourcePackets ?? Array.Empty<byte[]>(),
            targetPackets ?? Array.Empty<byte[]>(),
            broadcastPackets ?? Array.Empty<byte[]>());
}
