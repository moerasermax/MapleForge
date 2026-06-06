using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Drops;

public sealed record DropServiceOptions(
    int ExpRate = 1,
    int DropRate = 1,
    int MesoRate = 1,
    int DropObjectIdBase = 1_000_000);

public sealed record MobKillRewards(int ExpGained, IReadOnlyList<MapDrop> SpawnedDrops)
{
    public static MobKillRewards Empty => new(0, Array.Empty<MapDrop>());
}

public interface IMobKillHandler
{
    MobKillRewards OnMobKilled(FieldInstance field, Player killer, Mob mob);
}

public enum DropPickupStatus
{
    NotFound,
    AlreadyPickedUp,
    NotAllowed,
    InventoryFull,
    Success,
}

public sealed record DropPickupResult(
    DropPickupStatus Status,
    MapDrop? Drop = null,
    Item? GainedItem = null,
    int GainedMeso = 0,
    InventoryType? InventoryType = null)
{
    public bool Success => Status == DropPickupStatus.Success;
}

/// <summary>怪死獎勵用例：給擊殺者 EXP、依掉落表生成 MapDrop、處理 c2s 拾取。</summary>
public sealed class DropService : IMobKillHandler
{
    private const int DropChanceDenominator = 999_999;

    private readonly IMonsterDropCatalog _catalog;
    private readonly DropServiceOptions _options;

    public DropService(IMonsterDropCatalog catalog, DropServiceOptions? options = null)
    {
        _catalog = catalog;
        _options = options ?? new DropServiceOptions();
    }

    public MobKillRewards OnMobKilled(FieldInstance field, Player killer, Mob mob)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(killer);
        ArgumentNullException.ThrowIfNull(mob);

        var expGained = killer.GainExp(ScalePositive(mob.Stats.Exp, _options.ExpRate));
        var drops = SpawnDropsFromMonster(field, killer, mob);

        return expGained == 0 && drops.Count == 0
            ? MobKillRewards.Empty
            : new MobKillRewards(expGained, drops);
    }

    public DropPickupResult TryPickup(FieldInstance field, Player player, int dropObjectId)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(player);

        if (field.Get(dropObjectId) is not MapDrop drop)
        {
            return new DropPickupResult(DropPickupStatus.NotFound);
        }

        if (drop.IsPickedUp)
        {
            return new DropPickupResult(DropPickupStatus.AlreadyPickedUp, drop);
        }

        if (!CanPickUp(player, drop))
        {
            return new DropPickupResult(DropPickupStatus.NotAllowed, drop);
        }

        if (drop.IsMeso)
        {
            if (!drop.TryMarkPickedUp())
            {
                return new DropPickupResult(DropPickupStatus.AlreadyPickedUp, drop);
            }

            player.GainMeso(drop.Meso);
            field.Remove(drop.ObjectId);
            return new DropPickupResult(DropPickupStatus.Success, drop, GainedMeso: drop.Meso);
        }

        if (drop.Item is null)
        {
            return new DropPickupResult(DropPickupStatus.NotFound);
        }

        var type = Player.InventoryTypeOf(drop.Item.ItemId);
        var gained = player.GainDropItem(drop.Item);
        if (gained is null)
        {
            return new DropPickupResult(DropPickupStatus.InventoryFull, drop, InventoryType: type);
        }

        if (!drop.TryMarkPickedUp())
        {
            return new DropPickupResult(DropPickupStatus.AlreadyPickedUp, drop);
        }

        field.Remove(drop.ObjectId);
        return new DropPickupResult(DropPickupStatus.Success, drop, gained, InventoryType: type);
    }

    private IReadOnlyList<MapDrop> SpawnDropsFromMonster(FieldInstance field, Player killer, Mob mob)
    {
        var spawned = new List<MapDrop>();
        var ownerId = killer.Character.Id;
        var dropType = (byte)0; // Java: solo non-FFA monster drop.
        var sequence = 1;

        foreach (var entry in _catalog.RetrieveDrop(mob.Definition.MonsterId))
        {
            if (entry.ItemId == 0 || entry.Chance <= 0)
            {
                continue;
            }

            var chance = ScaleChance(entry.Chance, _options.DropRate);
            if (Random.Shared.Next(DropChanceDenominator) >= chance)
            {
                continue;
            }

            var item = CreateItem(entry);
            var drop = MapDrop.ForItem(
                AllocateDropObjectId(field),
                item,
                GetDropPosition(mob, sequence, dropType),
                mob.Position,
                mob.ObjectId,
                ownerId,
                dropType,
                questId: entry.QuestId);

            field.Add(drop);
            spawned.Add(drop);
            sequence++;
        }

        var level = Math.Max(1, (int)mob.Stats.Level);
        var meso = ScalePositive(Random.Shared.Next(level) + level, _options.MesoRate);
        if (meso > 0)
        {
            var drop = MapDrop.ForMeso(
                AllocateDropObjectId(field),
                meso,
                GetMesoDropPosition(mob, sequence),
                mob.Position,
                mob.ObjectId,
                ownerId,
                dropType);

            field.Add(drop);
            spawned.Add(drop);
        }

        return spawned;
    }

    private int AllocateDropObjectId(FieldInstance field)
    {
        var next = Math.Max(_options.DropObjectIdBase, field.Objects.Select(static o => o.ObjectId).DefaultIfEmpty(0).Max() + 1);
        while (field.Get(next) is not null)
        {
            next++;
        }

        return next;
    }

    private static bool CanPickUp(Player player, MapDrop drop)
    {
        if (drop.DropType >= 2)
        {
            return true;
        }

        return drop.OwnerId == player.Character.Id;
    }

    private static Item CreateItem(MonsterDropEntry entry)
    {
        var quantity = GetQuantity(entry);
        if (Player.InventoryTypeOf(entry.ItemId) == InventoryType.Equip)
        {
            return new Equip { ItemId = entry.ItemId, Quantity = 1 };
        }

        return new Item { ItemId = entry.ItemId, Quantity = quantity };
    }

    private static short GetQuantity(MonsterDropEntry entry)
    {
        if (entry.MaximumQuantity == 1)
        {
            return 1;
        }

        var range = Math.Abs(entry.MaximumQuantity - entry.MinimumQuantity);
        var rolled = Random.Shared.Next(range <= 0 ? 1 : range) + entry.MinimumQuantity;
        return (short)Math.Clamp(rolled, 1, short.MaxValue);
    }

    private static int ScaleChance(int chance, int rate)
        => Math.Clamp(ScalePositive(chance, rate), 0, DropChanceDenominator);

    private static int ScalePositive(int value, int rate)
    {
        if (value <= 0 || rate <= 0)
        {
            return 0;
        }

        return (int)Math.Min(int.MaxValue, (long)value * rate);
    }

    private static Position GetDropPosition(Mob mob, int sequence, byte dropType)
    {
        var step = dropType == 3 ? 40 : 25;
        var offset = sequence % 2 == 0
            ? step * (sequence + 1) / 2
            : -(step * (sequence / 2));

        return new Position(ClampShort(mob.Position.X + offset), mob.Position.Y, 0, mob.Position.Foothold);
    }

    private static Position GetMesoDropPosition(Mob mob, int sequence)
        => new(ClampShort(mob.Position.X - (25 * (sequence / 2))), mob.Position.Y, 0, mob.Position.Foothold);

    private static short ClampShort(int value)
        => (short)Math.Clamp(value, short.MinValue, short.MaxValue);
}
