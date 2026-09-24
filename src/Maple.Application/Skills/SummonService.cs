using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Skills;

/// <summary>P081：召喚獸出生結果；<see cref="Replaced"/> 是同技能重複施放時被換掉的舊召喚獸（呼叫端要先廣播移除）。</summary>
public sealed record SummonSpawnResult(Summon Summon, Summon? Replaced);

/// <summary>P083：主人離開地圖時卸下的召喚獸。<see cref="Cancelled"/> 要連同 buff 一起取消並廣播移除；
/// <see cref="Carried"/>（跟隨型）只從舊地圖靜默移出，進新地圖後重新出生（P084）。</summary>
public sealed record DetachedSummons(IReadOnlyList<Summon> Cancelled, IReadOnlyList<Summon> Carried);

/// <summary>
/// P081：召喚技能施放成功後建立地圖上的召喚獸。對照 Java <c>MapleStatEffect.applyTo</c>：
/// <c>getSummonMovementType() != null</c> → <c>new MapleSummon(applyfrom, this, pos, type)</c> →
/// <c>map.spawnSummon</c> + <c>addHP(x)</c>（暗黑之魂 1321007 再 +1）。重複施放同技能時 Java 先在
/// <c>applyBuffEffect</c> 的 <c>cancelEffect</c> → <c>deregisterBuffStats</c> 移除舊召喚獸。
/// 呼叫端負責 <c>lock(field)</c>。
/// </summary>
public sealed class SummonService
{
    public const int BeholderSkillId = 1321007;

    /// <summary>召喚獸物件 ID 起點（與掉落物 1_000_000 起點錯開，實際分配仍會避開已使用 ID）。</summary>
    public const int SummonObjectIdBase = 2_000_000;

    public SummonSpawnResult? TrySpawn(
        FieldInstance field,
        Player owner,
        int skillId,
        byte skillLevel,
        MapleStatEffect effect,
        Position position)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(effect);

        var movementType = Summon.GetMovementType(skillId);
        if (movementType is null)
        {
            return null;
        }

        var replaced = field.Objects
            .OfType<Summon>()
            .FirstOrDefault(s => s.OwnerId == owner.Character.Id && s.SkillId == skillId);
        if (replaced is not null)
        {
            field.Remove(replaced.ObjectId);
        }

        var hp = effect.X + (skillId == BeholderSkillId ? 1 : 0);
        var summon = new Summon(
            AllocateObjectId(field),
            skillId,
            skillLevel,
            owner.Character.Id,
            (short)Math.Clamp(hp, 0, short.MaxValue),
            movementType.Value,
            position);
        field.Add(summon);
        return new SummonSpawnResult(summon, replaced);
    }

    /// <summary>
    /// P082：SUMMON/PUPPET buff 取消或到期時移除對應召喚獸（對照 Java <c>MapleCharacter.deregisterBuffStats</c>：
    /// SUMMON/PUPPET 分支以 buff 來源技能 ID 找 <c>summons.get(sourceId)</c> → <c>removeSummon(summon, true)</c> →
    /// <c>map.removeMapObject</c>）。回傳被移除的召喚獸，呼叫端廣播。呼叫端負責 <c>lock(field)</c>。
    /// </summary>
    public IReadOnlyList<Summon> RemoveForCancelledBuffs(FieldInstance field, int ownerId, IEnumerable<PlayerBuffCancellation> cancellations)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(cancellations);

        var sourceIds = cancellations
            .Where(static c => c.Stats.Contains(MapleBuffStat.SUMMON) || c.Stats.Contains(MapleBuffStat.PUPPET))
            .Select(static c => c.SourceId)
            .ToHashSet();
        if (sourceIds.Count == 0)
        {
            return Array.Empty<Summon>();
        }

        var removed = field.Objects
            .OfType<Summon>()
            .Where(s => s.OwnerId == ownerId && sourceIds.Contains(s.SkillId))
            .ToArray();
        foreach (var summon in removed)
        {
            field.Remove(summon.ObjectId);
        }

        return removed;
    }

    /// <summary>
    /// P083：主人離開 field 時卸下其所有召喚獸。對照 Java <c>MapleMap.removePlayer</c>：
    /// <c>cancelEffectFromBuffStat(PUPPET)</c>；STATIONARY/CIRCLE_STATIONARY/WALK_STATIONARY → 取消 SUMMON buff
    /// （deregisterBuffStats 會廣播 removeSummon）；其他（跟隨型）→ <c>setChangedMap(true)</c> + <c>removeMapObject</c>
    /// （不廣播）。呼叫端負責 <c>lock(field)</c>。
    /// </summary>
    public DetachedSummons DetachOwnerSummons(FieldInstance field, int ownerId)
    {
        ArgumentNullException.ThrowIfNull(field);

        var owned = field.Objects.OfType<Summon>().Where(s => s.OwnerId == ownerId).ToArray();
        if (owned.Length == 0)
        {
            return new DetachedSummons(Array.Empty<Summon>(), Array.Empty<Summon>());
        }

        var cancelled = new List<Summon>();
        var carried = new List<Summon>();
        foreach (var summon in owned)
        {
            field.Remove(summon.ObjectId);
            if (summon.IsPuppet || summon.MovementType is SummonMovementType.Stationary
                    or SummonMovementType.CircleStationary or SummonMovementType.WalkStationary)
            {
                cancelled.Add(summon);
            }
            else
            {
                carried.Add(summon);
            }
        }

        return new DetachedSummons(cancelled, carried);
    }

    /// <summary>
    /// P085：跟隨型召喚獸隨主人進入新 field。對照 Java <c>MapleMap.addPlayer</c>：<c>getStatForBuff(SUMMON)</c> 仍在
    /// → <c>summon.setPosition(chr.getPosition())</c> → <c>spawnSummon</c>（新地圖重新分配物件 ID）。buff 已不在則回 null。
    /// 呼叫端負責 <c>lock(field)</c>。
    /// </summary>
    public Summon? Reattach(FieldInstance field, Player owner, Summon carried, Position position)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(carried);

        var buffActive = owner.ActiveBuffs.Any(b => b.SourceId == carried.SkillId && b.Stat == MapleBuffStat.SUMMON);
        if (!buffActive || carried.OwnerId != owner.Character.Id)
        {
            return null;
        }

        var summon = new Summon(
            AllocateObjectId(field),
            carried.SkillId,
            carried.SkillLevel,
            carried.OwnerId,
            carried.Hp,
            carried.MovementType,
            position);
        field.Add(summon);
        return summon;
    }

    private static int AllocateObjectId(FieldInstance field)
    {
        var next = Math.Max(SummonObjectIdBase, field.Objects.Select(static o => o.ObjectId).DefaultIfEmpty(0).Max() + 1);
        while (field.Get(next) is not null)
        {
            next++;
        }

        return next;
    }
}
