using Maple.Application.Maps;
using Maple.Core.Drops;
using Maple.Core.Maps;
using Maple.Core.Skills;
using Maple.Application.Parties;
using Maple.Core.Inventory;
using Maple.Core.Items;
using Maple.Core.World;

namespace Maple.Application.Drops;

public sealed record DropServiceOptions(
    int ExpRate = 1,
    int DropRate = 1,
    int MesoRate = 1,
    int DropObjectIdBase = 1_000_000);

/// <summary>
/// P150：擊殺者以外的經驗領取者（呼叫端送到對方 session）。P151：<paramref name="Exp"/> 為實際獲得總經驗；
/// 經驗訊息的顯示值 <paramref name="ShownExp"/>（Java gain + Additional_Bonus_EXP）與隊伍加成欄位。
/// </summary>
public sealed record MobExpAward(
    Player Receiver,
    int Exp,
    bool HighestDamage,
    PlayerStatsMutation? StatsMutation,
    int ShownExp = 0,
    int PartyExp = 0,
    int PartyBonusExp = 0);

/// <param name="ExpGained">擊殺者本人獲得的經驗（P150 起依傷害分配，可能為 0）。</param>
/// <param name="KillerHighestDamage">P150：擊殺者是否為最高傷害者（經驗訊息 white 旗標）。</param>
/// <param name="OtherAwards">P150：其他攻擊者／隊友的經驗。</param>
public sealed record MobKillRewards(
    int ExpGained,
    IReadOnlyList<MapDrop> SpawnedDrops,
    PlayerStatsMutation? StatsMutation = null,
    bool KillerHighestDamage = true,
    IReadOnlyList<MobExpAward>? OtherAwards = null,
    MobExpAward? KillerAward = null)
{
    public static MobKillRewards Empty => new(0, Array.Empty<MapDrop>());
}

public interface IMobKillHandler
{
    MobKillRewards OnMobKilled(FieldInstance field, Player killer, Mob mob);

    /// <summary>P144：神偷偷錢（Java <c>DamageParse.handlePickPocket</c>）；預設不掉落。</summary>
    IReadOnlyList<MapDrop> OnPickPocket(FieldInstance field, Player attacker, Mob mob, IReadOnlyList<int> damageLines, int maxMeso, int prop)
        => [];
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
    private readonly IEquipTemplateProvider? _equipTemplates;
    private readonly Random _random;
    private readonly MapService? _maps;
    private readonly Maple.Application.Combat.PlayerLocalStatsCalculator? _localStats;

    /// <summary>P165：只在會升級時計算 Java levelUp 的技能／總 INT 加成。</summary>
    private LevelUpBonus LevelUpBonusFor(Player player, int amount)
        => _localStats is not null && player.WouldLevelUp(amount) ? _localStats.ComputeLevelUpBonus(player) : default;

    public DropService(
        IMonsterDropCatalog catalog,
        DropServiceOptions? options = null,
        TimeProvider? timeProvider = null,
        IPartyRegistry? parties = null,
        IEquipTemplateProvider? equipTemplates = null,
        Random? random = null,
        MapService? maps = null,
        Maple.Application.Combat.PlayerLocalStatsCalculator? localStats = null)
    {
        _maps = maps;
        _localStats = localStats;
        _catalog = catalog;
        _options = options ?? new DropServiceOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _parties = parties;
        _equipTemplates = equipTemplates;
        _random = random ?? Random.Shared;
    }

    public MobKillRewards OnMobKilled(FieldInstance field, Player killer, Mob mob)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(killer);
        ArgumentNullException.ThrowIfNull(mob);

        var awards = DistributeExp(field, killer, mob);
        var killerAward = awards.FirstOrDefault(a => ReferenceEquals(a.Receiver, killer));
        var others = awards.Where(a => !ReferenceEquals(a.Receiver, killer)).ToArray();
        // P152：Java killMonster 的 dropOwner = killBy 回傳的 highestDamageChar（最高傷害且領到經驗者），否則擊殺者。
        var dropOwner = awards.FirstOrDefault(static a => a.HighestDamage)?.Receiver ?? killer;
        var drops = SpawnDropsFromMonster(field, dropOwner, mob);

        return killerAward is null && others.Length == 0 && drops.Count == 0
            ? MobKillRewards.Empty
            : new MobKillRewards(
                killerAward?.Exp ?? 0,
                drops,
                killerAward?.StatsMutation,
                killerAward?.HighestDamage ?? false,
                others.Length > 0 ? others : null,
                killerAward);
    }

    /// <summary>
    /// P150：對照 Java <c>MapleMonster.killBy</c> 的經驗分配：攻擊者依「有隊伍 → 隊伍 entry、否則個人 entry」分組
    /// （MapleForge 以擊殺當下的隊伍分組，Java 以每次攻擊時的隊伍）；最高傷害 entry 取第一個嚴格最大者；
    /// 每個 entry <c>baseExp = ceil(總經驗 * entry 傷害 / 怪物最大 HP)</c>。
    /// 個人：仍在本圖且存活才給。隊伍：對 entry 內每位（仍在本圖的）攻擊者，取同隊且
    /// <c>攻擊者等級 - 隊友等級 &lt;= 5 || 怪物等級 - 隊友等級 &lt;= 5</c>、在本圖存活的隊友為 expApplicable，
    /// <c>expFraction = baseExp * (個人傷害 / 隊伍總傷) / (人數 + 1)</c>，攻擊者權重 2.0、其他 0.3，
    /// 等級修正 <c>min(1, 等級 / 平均等級)</c>（本身也有攻擊則 1），<c>Math.round</c> 累加；最高傷害旗標只給該隊最高傷害者。
    /// 之後每位領取者走 <c>gainExpMonster</c> 的基礎部分：詛咒減半 → 伺服器倍率（隊伍加成等見後續）。
    /// </summary>
    private IReadOnlyList<MobExpAward> DistributeExp(FieldInstance field, Player killer, Mob mob)
    {
        var damages = mob.AttackerDamages;
        if (damages.Count == 0)
        {
            // 沒有傷害紀錄（例如直接呼叫 OnMobKilled 的舊路徑）：全歸擊殺者。
            damages = [(killer.Character.Id, mob.Stats.MaxHp)];
        }

        var entries = new List<(int? PartyId, List<(int CharacterId, long Damage)> Members, long Total)>();
        foreach (var (characterId, damage) in damages)
        {
            var partyId = _parties?.GetPartyForCharacter(characterId)?.Id;
            var index = partyId is null ? -1 : entries.FindIndex(e => e.PartyId == partyId);
            if (index < 0)
            {
                entries.Add((partyId, [(characterId, damage)], damage));
            }
            else
            {
                var entry = entries[index];
                entry.Members.Add((characterId, damage));
                entries[index] = (entry.PartyId, entry.Members, entry.Total + damage);
            }
        }

        var highestIndex = -1;
        long highestDamage = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Total > highestDamage)
            {
                highestIndex = i;
                highestDamage = entries[i].Total;
            }
        }

        var raw = new List<(Player Receiver, int Exp, bool Highest, int PartySize)>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var baseExp = (int)Math.Ceiling(mob.Stats.Exp * ((double)entry.Total / mob.Stats.MaxHp));
            if (entry.PartyId is null)
            {
                if (FindPlayer(field, entry.Members[0].CharacterId) is { IsAlive: true } single)
                {
                    raw.Add((single, baseExp, i == highestIndex, 0));
                }

                continue;
            }

            raw.AddRange(DistributePartyExp(field, mob, entry.PartyId.Value, entry.Members, entry.Total, baseExp, i == highestIndex));
        }

        var awards = new List<MobExpAward>(raw.Count);
        foreach (var (receiver, exp, highest, partySize) in raw)
        {
            if (exp <= 0)
            {
                continue;
            }

            var gain = ComputeMonsterExpGain(receiver, exp, partySize, mob);
            if (gain.Total > 0)
            {
                awards.Add(new MobExpAward(
                    receiver,
                    gain.Total,
                    highest,
                    receiver.GainExperience(gain.Total, bonus: LevelUpBonusFor(receiver, gain.Total)),
                    gain.Shown,
                    gain.PartyExp,
                    gain.PartyBonusExp));
            }
        }

        return awards;
    }

    /// <summary>
    /// P151：對照 Java <c>MapleCharacter.gainExpMonster</c> 的經驗組成：詛咒減半 → 10 級以下職業倍率（狂狼／皇家騎士團 5，
    /// 其他 1）否則伺服器倍率 → <c>Additional</c>：挑釁 SHOWDOWN ×(1 + x/100)、神聖祈禱 ×(1 + 值/(祭司 2311003 且組隊人數 &lt; 2 ? 500 : 100))、
    /// 經驗 buff ×<c>(int)((realExpBuff - 100 ≤ 0 ? 100 : realExpBuff - 100) / 100)</c>（int 複合指定截斷），再扣掉 gain、最低 0 →
    /// 組隊經驗（人數 &gt; 1）<c>gain * (5 * (n * (3 + (1 + n) / 2)) - 20) / 100.0</c>（n ≤ 6）→ 組隊額外（白金戒指 80%）。
    /// VIP、職業加成、道具／精靈墜飾、活動、結婚經驗未移植（0）。顯示值 = gain + Additional。
    /// </summary>
    private (int Total, int Shown, int PartyExp, int PartyBonusExp) ComputeMonsterExpGain(Player receiver, int exp, int partySize, Mob mob)
    {
        long gain = receiver.HasDisease(PlayerDisease.Curse) ? exp / 2 : exp;
        var job = receiver.Character.Job;
        gain *= receiver.Character.Level < 10
            ? (job is >= 2000 and <= 2112 and not 2001 || job is >= 1000 and < 2000 ? 5 : 1)
            : Math.Max(0, _options.ExpRate);
        gain = Math.Min(int.MaxValue, gain);

        var additional = (int)gain;
        if (mob.Statuses.TryGetValue(MonsterStatus.Showdown, out var showdown))
        {
            additional = (int)(additional * (1 + showdown.Value / 100.0));
        }

        if (receiver.ActiveBuffs.FirstOrDefault(static b => b.Stat == MapleBuffStat.HOLY_SYMBOL) is { } holySymbol)
        {
            var divisor = holySymbol.SourceId == HolySymbolPriestSkillId && partySize < 2 ? 500.0 : 100.0;
            additional = (int)(additional * (1 + holySymbol.Value / divisor));
        }

        var realExpBuff = 100.0 + (receiver.GetBuffValue(MapleBuffStat.EXPRATE) ?? 0);
        var lastExp = realExpBuff - 100.0 <= 0 ? 100 : realExpBuff - 100;
        additional = (int)(additional * (double)(int)(lastExp / 100.0));
        additional = Math.Max(0, additional - (int)gain);

        var partyExp = 0;
        if (partySize > 1)
        {
            var n = Math.Min(partySize, 6);
            partyExp = (int)(gain * ((5 * (n * (3 + (1 + n) / 2)) - 20) / 100.0));
        }

        var premiumPercent = receiver.Character.Equips.Any(static e => e.ItemId is 1112127 or 1112918) ? 80 : 0;
        var partyBonus = (int)(gain / 100.0 * premiumPercent);
        var total = Math.Min(int.MaxValue, gain + additional + partyExp + partyBonus);
        return ((int)total, (int)Math.Min(int.MaxValue, gain + additional), partyExp, partyBonus);
    }

    public const int HolySymbolPriestSkillId = 2311003;

    private IEnumerable<(Player Receiver, int Exp, bool Highest, int PartySize)> DistributePartyExp(
        FieldInstance field,
        Mob mob,
        int partyId,
        IReadOnlyList<(int CharacterId, long Damage)> members,
        long totalDamage,
        int baseExp,
        bool mostDamage)
    {
        var party = _parties?.GetParty(partyId);
        var attackerIds = members.Select(static m => m.CharacterId).ToHashSet();
        var expMap = new Dictionary<Player, int>();
        var partySizes = new Dictionary<Player, int>();
        Player? highest = null;
        long highestDamage = 0;
        foreach (var (attackerId, damage) in members)
        {
            if (FindPlayer(field, attackerId) is not { } attacker || party is null)
            {
                continue;
            }

            var applicable = party.Members
                .Select(m => FindPlayer(field, m.CharacterId))
                .OfType<Player>()
                .Where(p => p.IsAlive
                    && (attacker.Character.Level - p.Character.Level <= 5 || mob.Stats.Level - p.Character.Level <= 5))
                .ToArray();
            double averageLevel = applicable.Sum(static p => (double)p.Character.Level);
            if (applicable.Length > 1)
            {
                averageLevel /= applicable.Length;
            }

            if (damage > highestDamage)
            {
                highest = attacker;
                highestDamage = damage;
            }

            var innerBaseExp = baseExp * ((double)damage / totalDamage);
            var expFraction = innerBaseExp / (applicable.Length + 1);
            foreach (var receiver in applicable)
            {
                var weight = ReferenceEquals(receiver, attacker) ? 2.0 : 0.3;
                var levelMod = receiver.Character.Level / averageLevel;
                if (levelMod > 1.0 || attackerIds.Contains(receiver.Character.Id))
                {
                    levelMod = 1.0;
                }

                expMap[receiver] = expMap.GetValueOrDefault(receiver) + (int)JavaRound(expFraction * weight * levelMod);
                // Java ExpMap.ptysize = expApplicable.size() + added_partyinc（隊伍加成道具未移植 → 0）。
                partySizes[receiver] = applicable.Length;
            }
        }

        return expMap.Select(p => (p.Key, p.Value, mostDamage && ReferenceEquals(p.Key, highest), partySizes.GetValueOrDefault(p.Key)));
    }

    private static Player? FindPlayer(FieldInstance field, int characterId)
        => field.Players.FirstOrDefault(p => p.Character.Id == characterId);

    /// <summary>Java <c>Math.round(double)</c>：<c>floor(x + 0.5)</c>。</summary>
    private static double JavaRound(double value) => Math.Floor(value + 0.5);

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

        if (!CanPickUp(player, drop, field.Everlast))
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
            // P100：Java spawnItemDrop/spawnMesoDrop 在 everlast 地圖不對玩家掉落 registerExpire。
            if (!drop.ShouldExpire(now) || (field.Everlast && drop.PlayerDrop))
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
            // P100：Java spawnMesoDrop 在 everlast 地圖不對玩家掉落 registerFFA。
            if (!drop.ShouldBecomeFfa(now) || (field.Everlast && drop.PlayerDrop))
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
            // P144：Java spawnMesoDrop → calcDropPos(position, position)。
            CalcDropPos(field, player.Position.X, player.Position.Y, player.Position),
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
        // 對照 Java dropFromMonster 的 droptype 公式：
        // explosiveReward → 3（爆炸式掉落，間距 40，任何人可撿）；publicReward → 2（任何人可撿）（P096）；
        // 有隊伍 → 1（隊伍限定，P069 的 FFA 排程器 30 秒後會自動開放）；否則 → 0（限定擊殺者）（P070）。
        var dropType = mob.Stats.ExplosiveReward ? (byte)3
            : mob.Stats.FfaLoot ? (byte)2
            : _parties?.IsCharacterInParty(ownerId) == true ? (byte)1
            : (byte)0;
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
            var basePosition = GetDropPosition(mob, sequence, dropType);
            var drop = MapDrop.ForItem(
                AllocateDropObjectId(field),
                item,
                // P144：Java spawnMobDrop(idrop, calcDropPos(pos, mob.getPosition()), ...)。
                CalcDropPos(field, basePosition.X, basePosition.Y, mob.Position),
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
                MesoDropPosition(field, mob, sequence),
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

    /// <summary>
    /// P144：對照 Java <c>DamageParse.handlePickPocket</c>：每一行（客戶端原始傷害）擲偷錢 4211003 的機率，成功則在
    /// <c>(怪物 x + rand(100) - 50, 怪物 y)</c> 經 calcDropPos 掉
    /// <c>min((int)max(傷害 / 20000.0 * maxmeso, 1), maxmeso)</c> 楓幣（主人 = 攻擊者、dropType 0、非玩家掉落）。
    /// </summary>
    public IReadOnlyList<MapDrop> OnPickPocket(FieldInstance field, Player attacker, Mob mob, IReadOnlyList<int> damageLines, int maxMeso, int prop)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(mob);

        var drops = new List<MapDrop>();
        foreach (var line in damageLines)
        {
            if (prop != 100 && _random.Next(99) >= prop)
            {
                continue;
            }

            var meso = Math.Min((int)Math.Max(line / 20000.0 * maxMeso, 1.0), maxMeso);
            var x = (int)(mob.Position.X + _random.Next(100) - 50.0);
            var drop = MapDrop.ForMeso(
                AllocateDropObjectId(field),
                meso,
                CalcDropPos(field, x, mob.Position.Y, new Position(ClampShort(x), mob.Position.Y, 0, mob.Position.Foothold)),
                mob.Position,
                mob.ObjectId,
                attacker.Character.Id,
                dropType: 0,
                _timeProvider.GetUtcNow());
            field.Add(drop);
            drops.Add(drop);
        }

        return drops;
    }

    /// <summary>Java <c>calcDropPos</c>；未注入 MapService 或找不到踏腳石時用 fallback。</summary>
    private Position CalcDropPos(FieldInstance field, int x, int y, Position fallback)
        => _maps is not null && FootholdMath.CalcDropPos(_maps.LoadMap(field.MapId).Footholds, x, y) is { } point
            ? new Position(ClampShort(point.X), ClampShort(point.Y), 0, (short)point.FootholdId)
            : fallback;

    /// <summary>
    /// Java dropFromMonster 楓幣位置：<c>x = min(max(mobX - 25 * (d / 2), minDropX + 25), maxDropX - d * 25)</c>，
    /// 再 calcDropPos（fallback = 怪物位置）。無地圖資料時沿用原本的 <c>mobX - 25 * (d / 2)</c>。
    /// </summary>
    private Position MesoDropPosition(FieldInstance field, Mob mob, int sequence)
    {
        var footholds = _maps?.LoadMap(field.MapId).Footholds;
        if (footholds is not { Count: > 0 })
        {
            return GetMesoDropPosition(mob, sequence);
        }

        var (minX, maxX) = FootholdMath.DropXRange(footholds);
        var x = Math.Min(Math.Max(mob.Position.X - 25 * (sequence / 2), minX + 25), maxX - sequence * 25);
        return CalcDropPos(field, x, mob.Position.Y, mob.Position);
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

    /// <summary>
    /// 對照 Java <c>InventoryHandler.ItemPickup</c> 的拾取權判斷：
    /// 非主人時，(非玩家掉落 且 dropType 0) 或 (玩家掉落 且 everlast 地圖) → 擋（P100 補上玩家掉落分支：
    /// 一般地圖上玩家丟的東西任何人都能撿，原本 MapleForge 把它當 dropType 0 只讓主人撿）；
    /// 非玩家掉落 dropType 1 → 主人或同隊（P070）；dropType 2/3 → 任何人。
    /// </summary>
    private bool CanPickUp(Player player, MapDrop drop, bool everlast)
    {
        if (drop.OwnerId == player.Character.Id)
        {
            return true;
        }

        if (drop.PlayerDrop)
        {
            return !everlast;
        }

        if (drop.DropType >= 2)
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

    private Item CreateItem(MonsterDropEntry entry)
    {
        var quantity = GetQuantity(entry);
        if (Player.InventoryTypeOf(entry.ItemId) == InventoryType.Equip)
        {
            // 對照 Java MapleMap.dropFromMonster：randomizeStats(getEquipById)——WZ 預設數值再 ±10% 浮動（P103）。
            var equip = _equipTemplates?.Create(entry.ItemId) ?? new Equip { ItemId = entry.ItemId, Quantity = 1 };
            return EquipStatRandomizer.Randomize(equip, _random);
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
