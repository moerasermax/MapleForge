using Maple.Application.Skills;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Combat;

/// <summary>
/// P137：魔法攻擊的伺服器端傷害改寫（對照 Java <c>DamageParse.applyAttackMagic</c> 逐行區塊與
/// <c>calculateMaxMagicDamagePerHit</c>）。近戰／遠程不做：這份神諭在 <c>calculateMaxWeaponDamagePerHit</c> 結尾寫死
/// <c>elementalMaxDamagePerMonster = 9999999</c>（「直接寫死 -By Yc」），物理傷害上限等同關閉，照抄。
/// 未移植：魔法武器屬性加成（incRMAI 等，視為 100）、銳利之眼被動（視為 0）、GM 豁免（MapleForge 無 GM 等級）。
/// </summary>
public static class MagicDamageCap
{
    public const double AbsoluteMax = 199_999;

    /// <summary>
    /// Java 迴圈前的 <c>maxDamagePerHit</c>：蝸牛殼 40、金字塔 1，否則
    /// <c>totalMagic * (基礎 INT * 0.5 + (mad * 0.058)^2 + mad * 3.3) / 100</c>；最後 ×1.04。
    /// </summary>
    public static double BaseMaxDamagePerHit(int skillId, MapleStatEffect? effect, PlayerLocalStats stats, int baseInt)
    {
        double max;
        if (IsSnailThrow(skillId))
        {
            max = 40;
        }
        else if (SkillService.IsPyramidSkill(skillId))
        {
            max = 1;
        }
        else
        {
            var matk = effect?.Matk ?? 0;
            var v75 = matk * 0.058;
            max = stats.Magic * (baseInt * 0.5 + v75 * v75 + matk * 3.3) / 100;
        }

        return max * 1.04;
    }

    /// <summary>Java <c>calculateMaxMagicDamagePerHit</c>（每隻怪一次）。</summary>
    public static double MaxDamagePerHit(
        Player player,
        int skillId,
        Mob mob,
        PlayerLocalStats stats,
        Element element,
        double baseMax)
    {
        var mobStats = mob.Stats;
        var dLevel = Math.Max(mobStats.Level - player.Character.Level, 0);
        var accuracy = (int)(Math.Floor(stats.Int / 10.0) + Math.Floor(stats.Luk / 10.0));
        var minAccuracy = mobStats.Eva * (dLevel * 2 + 51) / 120;
        if (minAccuracy > accuracy && !IsSnailThrow(skillId) && !SkillService.IsPyramidSkill(skillId))
        {
            return 0;
        }

        // 魔法武器屬性加成 (x / 100) * element_* 預設 100 → 不變。
        var max = Effectiveness(mob, element) switch
        {
            ElementalEffectiveness.Immune => 1,
            ElementalEffectiveness.Weak => baseMax * 1.5,
            ElementalEffectiveness.Strong => baseMax * 0.5,
            _ => baseMax,
        };
        max -= mobStats.MagicDefense * 0.5;
        if (skillId == 21120006)
        {
            max *= 15;
        }

        if (skillId == 2211006)
        {
            max *= 2;
        }

        max += max * (mobStats.Boss ? stats.BossDamageRate : stats.DamageRate) / 100;
        if (IsSnailThrow(skillId))
        {
            max = 40;
        }
        else if (IsPharaohsWrath(skillId))
        {
            max = 1;
        }

        return max > AbsoluteMax ? AbsoluteMax : max < 0 ? 1 : max;
    }

    /// <summary>
    /// 一行魔法傷害的改寫；回 null 代表 Java <c>return</c>（整次攻擊中止：本隻與後續怪物不受傷、尾段 applyTo 不執行）。
    /// 順序：封鎖檢查（非 isElseSkill：新手技能超過 fixdamage；非狂狼且等級 &gt; 10 時 <c>eachd &gt;= getMaxDamage 且 &gt; 上限</c>、
    /// 剛好等於怪物最大 HP 豁免）→ fixedDamage／onlyNormalAttack → 凍結（21120006，非王）夾在怪物最大 HP →
    /// 一般怪：<c>eachd &gt; base</c> 且 <c>&gt; 上限×2</c> 時改為 <c>(int)(上限×2)</c>，結果 ≥ 10000 中止；
    /// 免疫／反射怪：<c>eachd &gt; base</c> 改為 <c>(int)base</c>。
    /// </summary>
    public static int? AdjustLine(
        int eachd,
        Player player,
        int skillId,
        MapleStatEffect? effect,
        Mob mob,
        double baseMax,
        double maxPerHit)
    {
        var mobStats = mob.Stats;
        if (!IsElseSkill(skillId))
        {
            if (IsNoviceSkill(skillId) && eachd > (effect?.FixDamage ?? 0))
            {
                return null;
            }

            var job = player.Character.Job;
            if (!IsAran(job) && player.Character.Level > 10)
            {
                var atk = MaxDamageByLevel(player.Character.Level, skillId);
                if (eachd >= atk && eachd > maxPerHit && eachd != mobStats.MaxHp)
                {
                    return null;
                }
            }
        }

        if (mobStats.FixedDamage != -1)
        {
            return mobStats.OnlyNormalAttack ? 0 : mobStats.FixedDamage;
        }

        if (mobStats.OnlyNormalAttack)
        {
            return 0;
        }

        if (mob.Statuses.TryGetValue(MonsterStatus.Freeze, out var freeze) && freeze.SkillId == 21120006 && !mobStats.Boss)
        {
            return eachd > mobStats.MaxHp ? (int)Math.Min(mobStats.MaxHp, int.MaxValue) : eachd;
        }

        var shielded = mob.Statuses.ContainsKey(MonsterStatus.DamageImmunity)
            || mob.Statuses.ContainsKey(MonsterStatus.MagicImmunity)
            || mob.Statuses.ContainsKey(MonsterStatus.MagicDamageReflect);
        if (!shielded)
        {
            if (eachd > baseMax && eachd > maxPerHit * 2)
            {
                var clamped = (int)(maxPerHit * 2);
                return clamped >= 10000 ? null : clamped;
            }

            return eachd;
        }

        return eachd > baseMax ? (int)baseMax : eachd;
    }

    /// <summary>Java <c>SkillConstants.getMaxDamage(chr, skill)</c>：依等級的單擊封鎖門檻（魔天一擊 ×1.2）。</summary>
    public static int MaxDamageByLevel(int level, int skillId)
    {
        var max = level switch
        {
            < 4 => 80,
            < 9 => 150,
            < 10 => 250,
            <= 15 => 600,
            <= 20 => 1000,
            <= 25 => 1500,
            <= 30 => 2200,
            <= 35 => 3200,
            <= 40 => 4000,
            <= 50 => 7000,
            <= 60 => 8000,
            _ => 200_000,
        };
        return skillId is 1001004 or 11001002 ? (int)(max * 1.2) : max;
    }

    /// <summary>Java 怪物 <c>getEffectiveness</c>：身上有 Doom 時一律 Normal，否則看模板。</summary>
    private static ElementalEffectiveness Effectiveness(Mob mob, Element element)
        => mob.Statuses.ContainsKey(MonsterStatus.Doom) ? ElementalEffectiveness.Normal : mob.Stats.GetEffectiveness(element);

    private static bool IsSnailThrow(int skillId) => skillId is 1000 or 10001000 or 20001000 or 20011000 or 30001000;

    private static bool IsPharaohsWrath(int skillId) => skillId is 1020 or 10001020 or 20001020;

    /// <summary>Java <c>GameConstants.Novice_Skill</c>。</summary>
    private static bool IsNoviceSkill(int skillId) => skillId is 1000 or 10001000 or 20001000;

    /// <summary>Java <c>GameConstants.isElseSkill</c>。</summary>
    private static bool IsElseSkill(int skillId)
        => skillId is 10001009 or 20001009 or 1009 or 1020 or 10001020 or 20001020 or 3221001 or 4211006;

    /// <summary>Java <c>GameConstants.isAran</c>。</summary>
    private static bool IsAran(int job) => job is >= 2000 and <= 2112 and not 2001;
}
