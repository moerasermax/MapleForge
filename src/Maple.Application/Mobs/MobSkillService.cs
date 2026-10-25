using Maple.Core.World;

namespace Maple.Application.Mobs;

/// <summary>怪物這次移動實際施放的技能（null＝沒有施放）。</summary>
public sealed record MobSkillUse(MobSkillDefinition Skill);

/// <summary>
/// P122：怪物技能挑選（對照 Java <c>MobHandler.MoveMonster</c> 的 useSkill 區塊）：從怪物技能清單隨機抽一個 →
/// <c>checkCurrentBuff</c> 未生效 → 冷卻（<c>ls == 0 || (now - ls &gt; cool &amp;&amp; !summonOnce)</c>，先登記冷卻）→
/// HP 百分比 ≤ 技能 hp 門檻才施放。效果（<c>applyEffect</c>）於後續 P 逐類補上；MP 消耗照 Java 在施放時扣。
/// </summary>
public sealed class MobSkillService
{
    private readonly IMobSkillCatalog _catalog;
    private readonly Random _random;

    public MobSkillService(IMobSkillCatalog catalog, Random? random = null)
    {
        _catalog = catalog;
        _random = random ?? Random.Shared;
    }

    public MobSkillUse? TryUseSkill(FieldInstance field, Mob mob, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(mob);

        var skills = mob.Stats.Skills;
        if (skills is null || skills.Count == 0)
        {
            return null;
        }

        var chosen = skills[_random.Next(skills.Count)];
        var skill = _catalog.Get(chosen.SkillId, chosen.Level);
        if (skill is null || IsBuffAlreadyActive(skill, mob, field))
        {
            return null;
        }

        var lastUsed = mob.GetLastSkillUsed(skill.SkillId);
        if (lastUsed is { } ls && !((now - ls).TotalMilliseconds > skill.CoolTimeMilliseconds && !skill.SummonOnce))
        {
            return null;
        }

        mob.SetLastSkillUsed(skill.SkillId, now, skill.CoolTimeMilliseconds);

        var hpPercent = (int)((float)mob.Hp / Math.Max(1, mob.Stats.MaxHp) * 100);
        if (hpPercent > skill.Hp)
        {
            return null;
        }

        mob.ConsumeMp(skill.MpCon);
        return new MobSkillUse(skill);
    }

    /// <summary>
    /// 逐字對照 Java <c>MobSkill.checkCurrentBuff</c>（含原碼 101/111/151 查 WEAPON_DEFENSE_UP、102/112/152 查
    /// MAGIC_ATTACK_UP，與 applyEffect 的對應交錯——照抄）。
    /// </summary>
    public static bool IsBuffAlreadyActive(MobSkillDefinition skill, Mob mob, FieldInstance field)
        => skill.SkillId switch
        {
            100 or 110 or 150 => mob.Statuses.ContainsKey(MonsterStatus.WeaponAttackUp),
            101 or 111 or 151 => mob.Statuses.ContainsKey(MonsterStatus.WeaponDefenseUp),
            102 or 112 or 152 => mob.Statuses.ContainsKey(MonsterStatus.MagicAttackUp),
            103 or 113 or 153 => mob.Statuses.ContainsKey(MonsterStatus.MagicDefenseUp),
            >= 140 and <= 145 => mob.Statuses.ContainsKey(MonsterStatus.DamageImmunity)
                || mob.Statuses.ContainsKey(MonsterStatus.MagicImmunity)
                || mob.Statuses.ContainsKey(MonsterStatus.WeaponImmunity),
            200 => field.Objects.OfType<Mob>().Count() >= skill.Limit,
            _ => false,
        };
}
