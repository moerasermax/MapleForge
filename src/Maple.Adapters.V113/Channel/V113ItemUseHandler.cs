using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Items;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public interface IV113ItemUseRandomSource
{
    int NextInt(int exclusiveMax);
}

public sealed class V113ItemUseRandomSource : IV113ItemUseRandomSource
{
    public int NextInt(int exclusiveMax) => Random.Shared.Next(exclusiveMax);
}

internal sealed record V113ItemUseContext
{
    public int ReturnMapId { get; init; }

    public bool CanUseReturnScroll { get; init; } = true;

    public bool CanUseSummonBag { get; init; } = true;

    public bool IsGm { get; init; }
}

internal sealed record V113ItemUseTargetMob(int ObjectId, int MonsterId, long Hp, long MaxHp)
{
    public static V113ItemUseTargetMob From(Mob mob) =>
        new(mob.ObjectId, mob.Stats.MonsterId, mob.Hp, mob.Stats.MaxHp);
}

internal sealed record V113ItemUseGainIntent(InventoryType Type, int ItemId, short Quantity, short Slot);

internal sealed record V113ItemUseResult
{
    public bool Applied { get; init; }

    public int? WarpMapId { get; init; }

    public IReadOnlyList<int> SpawnMonsterIds { get; init; } = Array.Empty<int>();

    public int? RemoveMonsterObjectId { get; init; }

    public IReadOnlyList<V113ItemUseGainIntent> GainItems { get; init; } = Array.Empty<V113ItemUseGainIntent>();

    public IReadOnlyList<InventoryQuantityMutation> InventoryMutations { get; init; } =
        Array.Empty<InventoryQuantityMutation>();

    public IReadOnlyList<byte[]> SelfPackets { get; init; } = Array.Empty<byte[]>();

    public IReadOnlyList<byte[]> BroadcastPackets { get; init; } = Array.Empty<byte[]>();

    public IReadOnlyList<string> SelfMessages { get; init; } = Array.Empty<string>();
}

public sealed class V113ItemUseHandler
{
    public const int ReturnMapSentinel = 999999999;

    private const string CatchHighHpMessage = "Monster HP is too high to catch.";

    private readonly IItemUseCatalog _catalog;
    private readonly IV113ItemUseRandomSource _random;

    public V113ItemUseHandler(IItemUseCatalog catalog, IV113ItemUseRandomSource? random = null)
    {
        _catalog = catalog;
        _random = random ?? new V113ItemUseRandomSource();
    }

    internal V113ItemUseResult HandleUseMountFood(PacketReader reader, Player player)
        => HandleUseMountFood(V113ItemUsePackets.ParseUseInventoryItem(reader), player);

    internal V113ItemUseResult HandleUseMountFood(V113UseItemRequest request, Player player)
    {
        var expGain = player.Mount is { Fatigue: > 0 } mount
            ? RollMountFoodExp(mount.Level)
            : 0;
        var result = player.UseMountFood(request.Slot, request.ItemId, expGain);
        if (!result.Applied || result.ConsumedItem is null || player.Mount is null)
        {
            return EnableOnly();
        }

        return new V113ItemUseResult
        {
            Applied = true,
            InventoryMutations = new[] { result.ConsumedItem },
            SelfPackets = new[]
            {
                V113ItemUsePackets.ModifyInventoryQuantity(result.ConsumedItem),
                V113StatsPackets.EnableActions(),
            },
            BroadcastPackets = new[]
            {
                V113ItemUsePackets.UpdateMount(player.Character.Id, player.Mount, result.LevelUp),
            },
        };
    }

    internal V113ItemUseResult HandleUseSummonBag(PacketReader reader, Player player, V113ItemUseContext context)
        => HandleUseSummonBag(V113ItemUsePackets.ParseUseInventoryItem(reader), player, context);

    internal V113ItemUseResult HandleUseSummonBag(V113UseItemRequest request, Player player, V113ItemUseContext context)
    {
        if (!player.IsAlive)
        {
            return EnableOnly();
        }

        if (!player.TryConsumeInventoryItem(
                InventoryType.Use,
                request.Slot,
                request.ItemId,
                1,
                out var consumed) ||
            consumed is null)
        {
            return EnableOnly();
        }

        player.FlushInventory();
        var spawnMonsterIds = new List<int>();
        if (context.IsGm || context.CanUseSummonBag)
        {
            var entries = _catalog.GetSummonBagMobs(request.ItemId);
            if (entries is not null)
            {
                foreach (var entry in entries)
                {
                    if (ShouldSpawnSummonBagMob(entry))
                    {
                        spawnMonsterIds.Add(entry.MobId);
                    }
                }
            }
        }

        return new V113ItemUseResult
        {
            Applied = true,
            SpawnMonsterIds = spawnMonsterIds,
            InventoryMutations = new[] { consumed },
            SelfPackets = new[]
            {
                V113ItemUsePackets.ModifyInventoryQuantity(consumed),
                V113StatsPackets.EnableActions(),
            },
        };
    }

    internal V113ItemUseResult HandleUseReturnScroll(PacketReader reader, Player player, V113ItemUseContext context)
        => HandleUseReturnScroll(V113ItemUsePackets.ParseUseInventoryItem(reader), player, context);

    internal V113ItemUseResult HandleUseReturnScroll(V113UseItemRequest request, Player player, V113ItemUseContext context)
    {
        if (!player.IsAlive || player.Character.MapId == 749040100 || !context.CanUseReturnScroll)
        {
            return EnableOnly();
        }

        var moveTo = _catalog.GetReturnScrollDestinationMapId(request.ItemId);
        if (moveTo is null)
        {
            return EnableOnly();
        }

        var targetMapId = moveTo.Value == ReturnMapSentinel ? context.ReturnMapId : moveTo.Value;
        if (targetMapId <= 0 || !CanUseReturnScrollBetween(player.Character.MapId, targetMapId))
        {
            return EnableOnly();
        }

        if (!player.TryConsumeInventoryItem(
                InventoryType.Use,
                request.Slot,
                request.ItemId,
                1,
                out var consumed) ||
            consumed is null)
        {
            return EnableOnly();
        }

        player.FlushInventory();
        return new V113ItemUseResult
        {
            Applied = true,
            WarpMapId = targetMapId,
            InventoryMutations = new[] { consumed },
            SelfPackets = new[] { V113ItemUsePackets.ModifyInventoryQuantity(consumed) },
        };
    }

    internal V113ItemUseResult HandleUseCatchItem(
        PacketReader reader,
        Player player,
        Func<int, V113ItemUseTargetMob?> mobResolver)
    {
        var request = V113ItemUsePackets.ParseUseCatchItem(reader);
        return HandleUseCatchItem(request, player, mobResolver(request.MobObjectId));
    }

    internal V113ItemUseResult HandleUseCatchItem(
        V113UseCatchItemRequest request,
        Player player,
        V113ItemUseTargetMob? target)
    {
        if (target is null || !HasMatchingUseItem(player, request.Slot, request.ItemId))
        {
            return EnableOnly();
        }

        return request.ItemId switch
        {
            2270004 => CatchHalfHpTarget(player, request, target, rewardItemId: 4001169),
            2270002 => CatchHalfHpTarget(player, request, target, rewardItemId: 0),
            2270000 when target.MonsterId == 9300101 => CatchSuccess(player, request, target, rewardItemId: 1902000),
            2270003 when target.MonsterId == 9500320 => CatchHalfHpTarget(player, request, target, rewardItemId: 0),
            _ => EnableOnly(),
        };
    }

    private V113ItemUseResult CatchHalfHpTarget(
        Player player,
        V113UseCatchItemRequest request,
        V113ItemUseTargetMob target,
        int rewardItemId)
    {
        if (target.Hp <= target.MaxHp / 2)
        {
            return CatchSuccess(player, request, target, rewardItemId);
        }

        return new V113ItemUseResult
        {
            SelfPackets = One(V113StatsPackets.EnableActions()),
            BroadcastPackets = One(V113ItemUsePackets.CatchMonster(target.MonsterId, request.ItemId, 0)),
            SelfMessages = new[] { CatchHighHpMessage },
        };
    }

    private V113ItemUseResult CatchSuccess(
        Player player,
        V113UseCatchItemRequest request,
        V113ItemUseTargetMob target,
        int rewardItemId)
    {
        if (!player.TryConsumeInventoryItem(
                InventoryType.Use,
                request.Slot,
                request.ItemId,
                1,
                out var consumed) ||
            consumed is null)
        {
            return EnableOnly();
        }

        var selfPackets = new List<byte[]> { V113ItemUsePackets.ModifyInventoryQuantity(consumed) };
        var gainIntents = new List<V113ItemUseGainIntent>();
        if (rewardItemId > 0)
        {
            var type = Player.InventoryTypeOf(rewardItemId);
            var item = player.GainItem(type, rewardItemId, 1);
            if (item is not null)
            {
                gainIntents.Add(new V113ItemUseGainIntent(type, rewardItemId, item.Quantity, item.Slot));
                selfPackets.Add(V113ItemUsePackets.ModifyInventoryAdd(type, item));
            }
        }

        player.FlushInventory();
        selfPackets.Add(V113StatsPackets.EnableActions());

        return new V113ItemUseResult
        {
            Applied = true,
            RemoveMonsterObjectId = target.ObjectId,
            GainItems = gainIntents,
            InventoryMutations = new[] { consumed },
            SelfPackets = selfPackets,
            BroadcastPackets = One(V113ItemUsePackets.CatchMonster(target.MonsterId, request.ItemId, 1)),
        };
    }

    private bool ShouldSpawnSummonBagMob(SummonBagMobEntry entry)
    {
        var probability = Math.Clamp(entry.Probability, 0, 100);
        return _random.NextInt(99) <= probability;
    }

    private int RollMountFoodExp(int level)
    {
        if (level is >= 1 and <= 7)
        {
            return _random.NextInt(10) + 15;
        }

        if (level is >= 8 and <= 15)
        {
            return _random.NextInt(13) + 7;
        }

        if (level is >= 16 and <= 24)
        {
            return _random.NextInt(23) + 9;
        }

        return _random.NextInt(28) + 12;
    }

    private static bool CanUseReturnScrollBetween(int currentMapId, int targetMapId)
    {
        var currentRegion = currentMapId / 10000000;
        var targetRegion = targetMapId / 10000000;

        if (targetRegion != 60 && currentRegion != 61)
        {
            if (targetRegion != 21 && currentRegion != 20)
            {
                if (targetRegion != currentRegion)
                {
                    if (targetMapId == 120000000 && currentMapId != 120000000)
                    {
                        return true;
                    }

                    if (targetMapId != 120000000 && currentMapId == 120000000)
                    {
                        return true;
                    }

                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasMatchingUseItem(Player player, short slot, int itemId)
    {
        var item = player.Inventory.By(InventoryType.Use).Get(slot);
        return item is not null && item.ItemId == itemId && item.Quantity > 0;
    }

    private static V113ItemUseResult EnableOnly() =>
        new() { SelfPackets = One(V113StatsPackets.EnableActions()) };

    private static IReadOnlyList<byte[]> One(byte[] packet) => new[] { packet };
}
