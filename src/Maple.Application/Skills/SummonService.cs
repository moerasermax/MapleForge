using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Skills;

/// <summary>P081：召喚獸出生結果；<see cref="Replaced"/> 是同技能重複施放時被換掉的舊召喚獸（呼叫端要先廣播移除）。</summary>
public sealed record SummonSpawnResult(Summon Summon, Summon? Replaced);

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
