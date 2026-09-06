using Maple.Application.Parties;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Drops;

public sealed record DropServiceOptions(
    int ExpRate = 1,
    int DropRate = 1,
    int MesoRate = 1,
    int DropObjectIdBase = 1_000_000);

public sealed record MobKillRewards(
    int ExpGained,
    IReadOnlyList<MapDrop> SpawnedDrops,
    PlayerStatsMutation? StatsMutation = null)
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

public enum MesoDropStatus
{
    InvalidAmount,
    NotAlive,
    NotEnoughMeso,
    Success,
}

public sealed record MesoDropResult(MesoDropStatus Status, MapDrop? Drop = null)
{
    public bool Success => Status == MesoDropStatus.Success;
}

/// <summary>怪死獎勵用例：給擊殺者 EXP、依掉落表生成 MapDrop、處理 c2s 拾取。</summary>
public sealed class DropService : IMobKillHandler
{
    private const int DropChanceDenominator = 999_999;

    private readonly IMonsterDropCatalog _catalog;
    private readonly DropServiceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IPartyRegistry? _parties;

    public DropService(
        IMonsterDropCatalog catalog,
        DropServiceOptions? options = null,
        TimeProvider? timeProvider = null,
        IPartyRegistry? parties = null)
    {
        _catalog = catalog;
        _options = options ?? new DropServiceOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _parties = parties;
    }

    public MobKillRewards OnMobKilled(FieldInstance field, Player killer, Mob mob)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(killer);
        ArgumentNullException.ThrowIfNull(mob);

        var expGained = ScalePositive(mob.Stats.Exp, _options.ExpRate);
        var statsMutation = expGained > 0
            ? killer.GainExperience(expGained)
            : null;
        var drops = SpawnDropsFromMonster(field, killer, mob);

        return expGained == 0 && drops.Count == 0
            ? MobKillRewards.Empty
            : new MobKillRewards(expGained, drops, statsMutation);
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

    /// <summary>
    /// P062（M4-2 世界 tick 第二步）：對照 Java <c>MapleMapItem.expire</c>——找出這個 field 裡已經超過
    /// <see cref="MapDrop.ExpireAfter"/> 還沒被撿走的掉落物，標記成已撿走並從場上移除，回傳被移除的
    /// 清單供呼叫端廣播 <c>REMOVE_ITEM_FROM_MAP</c>（animation=0/Expire）。刻意不移植 Java 的
    /// <c>randDrop</c>（過期後自動補生一個隨機獎勵箱）分支——那是掉落表系統的獨立旗標，MapleForge
    /// 目前沒有對應資料，留給後續評估。這個方法本身還不會被任何排程器呼叫（P063+ 才接上）。
    /// </summary>
    public IReadOnlyList<MapDrop> ExpireDrops(FieldInstance field, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(field);

        var expired = new List<MapDrop>();
        foreach (var drop in field.Objects.OfType<MapDrop>().ToArray())
        {
            if (!drop.ShouldExpire(now))
            {
                continue;
            }

            if (!drop.TryMarkPickedUp())
            {
                continue;
            }

            field.Remove(drop.ObjectId);
            expired.Add(drop);
        }

        return expired;
    }

    /// <summary>
    /// P069：對照 Java <c>World.handleMap</c> 的 <c>item.shouldFFA()</c> 分支——限定主人/隊伍的
    /// 掉落物滿 <see cref="MapDrop.FfaAfter"/>（30 秒）還沒被撿走，就轉成任何人可撿
    /// （<c>DropType=2</c>）。Java 這裡不廣播任何封包，純粹是伺服器內部狀態轉換，所以不需要
    /// 回傳值給呼叫端做廣播——但仍然回傳轉換清單方便測試直接驗證行為。
    /// </summary>
    public IReadOnlyList<MapDrop> PromoteFfaDrops(FieldInstance field, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(field);

        var promoted = new List<MapDrop>();
        foreach (var drop in field.Objects.OfType<MapDrop>().ToArray())
        {
            if (!drop.ShouldBecomeFfa(now))
            {
                continue;
            }

            drop.MarkFfa();
            promoted.Add(drop);
        }

        return promoted;
    }

    public MesoDropResult TryDropMeso(FieldInstance field, Player player, int meso)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(player);

        if (!player.IsAlive)
        {
            return new MesoDropResult(MesoDropStatus.NotAlive);
        }

        if (meso is < 10 or > 50_000)
        {
            return new MesoDropResult(MesoDropStatus.InvalidAmount);
        }

        if (player.Character.Meso < meso)
        {
            return new MesoDropResult(MesoDropStatus.NotEnoughMeso);
        }

        player.GainMeso(-meso);
        var drop = MapDrop.ForMeso(
            AllocateDropObjectId(field),
            meso,
            player.Position,
            player.Position,
            player.ObjectId,
            player.Character.Id,
            dropType: 0,
            _timeProvider.GetUtcNow(),
            playerDrop: true);

        field.Add(drop);
        return new MesoDropResult(MesoDropStatus.Success, drop);
    }

    private IReadOnlyList<MapDrop> SpawnDropsFromMonster(FieldInstance field, Player killer, Mob mob)
    {
        var spawned = new List<MapDrop>();
        var ownerId = killer.Character.Id;
        // P070：對照 Java dropFromMonster 的 droptype 公式（isExplosiveReward/isFfaLoot 兩個
        // 特殊怪物模板旗標 MapleForge 尚未有對應資料，暫不移植，屬另一個獨立範圍的缺口）：
        // 有隊伍 → 1（隊伍限定，P069 的 FFA 排程器 30 秒後會自動開放）；否則 → 0（限定擊殺者）。
        var dropType = _parties?.IsCharacterInParty(ownerId) == true ? (byte)1 : (byte)0;
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
                _timeProvider.GetUtcNow(),
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
                dropType,
                _timeProvider.GetUtcNow());

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

    private bool CanPickUp(Player player, MapDrop drop)
    {
        if (drop.DropType >= 2)
        {
            return true;
        }

        if (drop.OwnerId == player.Character.Id)
        {
            return true;
        }

        // P070：對照 Java PlayerHandler.PlayerPickup 的 dropType==1 分支——隊伍限定掉落物，
        // 擊殺者所在隊伍的其他成員也能撿（跟擊殺者本人的 OwnerId 判斷分開，兩者都要放行）。
        // dropType==0 沒有這個例外，忠實對照 Java（限定主人才能撿，隊友也不行）。
        if (drop.DropType != 1 || _parties is null)
        {
            return false;
        }

        var ownerParty = _parties.GetPartyForCharacter(drop.OwnerId);
        return ownerParty is not null && ownerParty.GetMember(player.Character.Id) is not null;
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
