using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Combat;

/// <summary>
/// P142：物理攻擊命中後由玩家 buff／被動／武器衍生的怪物異常（對照 Java <c>DamageParse.applyAttack:398-512</c>，
/// 在技能本身的 monsterStatus 之前）。每個狀態走 <see cref="Mob.TryApplyStatus"/>（屬性取該狀態來源技能的 elemAttr）。
/// </summary>
public sealed class OnHitStatusApplier
{
    public const int WeaponStatusChance = 25;
    public const int WeaponStatusDurationMilliseconds = 10_000;

    private static readonly int[] VenomSkills = [4120005, 4220005, 14110004];

    private readonly ISkillCatalog _skills;
    private readonly Random _random;

    public OnHitStatusApplier(ISkillCatalog skills, Random random)
    {
        _skills = skills;
        _random = random;
    }

    public IReadOnlyList<MonsterStatusEffect> Apply(Mob mob, Player attacker, int skillId, DateTimeOffset now)
    {
        var applied = new List<MonsterStatusEffect>();
        var boss = mob.Stats.Boss;

        if (IsThiefChargeSkill(skillId))
        {
            ApplyChargeSlow(mob, attacker, boss, now, applied);
            ApplyBodyPressure(mob, attacker, boss, now, applied);

            // Java：依序找第一個學了的飛毒殺，擲機率後 break（不論成功與否）。
            foreach (var venomId in VenomSkills)
            {
                if (attacker.GetSkillLevel(venomId) is var level and > 0)
                {
                    if (Effect(venomId, level) is { } venom && MakeChance(venom.Prop))
                    {
                        TryApplyPoison(mob, attacker, venomId, level, venom.DurationMilliseconds, now, applied);
                    }

                    break;
                }
            }
        }
        else if (IsAranChargeSkill(skillId))
        {
            ApplyChargeSlow(mob, attacker, boss, now, applied);
            ApplyBodyPressure(mob, attacker, boss, now, applied);
        }

        if (attacker.Character.Equips.FirstOrDefault(static e => e.Position == PlayerLocalStatsCalculator.WeaponSlot) is { } weapon
            && StatFromWeapon(weapon.ItemId) is { } weaponStat
            && _random.Next(100) < WeaponStatusChance)
        {
            var (x, statSkill) = weaponStat == MonsterStatus.Acc ? (-70, 3221006) : (-50, 3121007);
            TryApply(mob, new MonsterStatusEffect(weaponStat, x, statSkill, null, now.AddMilliseconds(WeaponStatusDurationMilliseconds)), applied);
        }

        if (BuffEffect(attacker, MapleBuffStat.BLIND) is { } blind && MakeChance(blind.Prop))
        {
            TryApply(mob, new MonsterStatusEffect(MonsterStatus.Acc, blind.X, blind.SourceId, null, now.AddMilliseconds(blind.Y * 1000.0)), applied);
        }

        if (BuffEffect(attacker, MapleBuffStat.HAMSTRING) is { } hamstring && MakeChance(hamstring.Prop))
        {
            TryApply(mob, new MonsterStatusEffect(MonsterStatus.Speed, hamstring.X, 3121007, null, now.AddMilliseconds(hamstring.Y * 1000.0)), applied);
        }

        // Java：聖騎士 121/122 的鬥氣來源是寒冰之劍 1211006 或 1211005 → FREEZE 1（持續 y × 2 秒，效果取目前技能等級）。
        if (attacker.Character.Job is 121 or 122 && ChargeSource(attacker) is (1211006 or 1211005) and var chargeSkill
            && Effect(chargeSkill, attacker.GetSkillLevel(chargeSkill)) is { } iceCharge)
        {
            TryApply(mob, new MonsterStatusEffect(MonsterStatus.Freeze, 1, chargeSkill, null, now.AddMilliseconds(iceCharge.Y * 2000.0)), applied);
        }

        return applied;
    }

    /// <summary>Java：WK_CHARGE 且非王 → SPEED（x = 鬥氣 x，持續 y 秒）。</summary>
    private void ApplyChargeSlow(Mob mob, Player attacker, bool boss, DateTimeOffset now, List<MonsterStatusEffect> applied)
    {
        if (!boss && BuffEffect(attacker, MapleBuffStat.WK_CHARGE) is { } charge)
        {
            TryApply(mob, new MonsterStatusEffect(MonsterStatus.Speed, charge.X, charge.SourceId, null, now.AddMilliseconds(charge.Y * 1000.0)), applied);
        }
    }

    /// <summary>Java：BODY_PRESSURE 且非王、擲機率、尚未 NEUTRALISE → NEUTRALISE 1（持續 x 秒）。</summary>
    private void ApplyBodyPressure(Mob mob, Player attacker, bool boss, DateTimeOffset now, List<MonsterStatusEffect> applied)
    {
        if (!boss && BuffEffect(attacker, MapleBuffStat.BODY_PRESSURE) is { } pressure && MakeChance(pressure.Prop)
            && !mob.Statuses.ContainsKey(MonsterStatus.Neutralise))
        {
            TryApply(mob, new MonsterStatusEffect(MonsterStatus.Neutralise, 1, pressure.SourceId, null, now.AddMilliseconds(pressure.X * 1000.0)), applied);
        }
    }

    /// <summary>Java <c>applyStatus(poison = true)</c>：HP &gt; 1 才計算毒傷（同 P113），持續 ≥ 2e9 改 5000。</summary>
    private void TryApplyPoison(Mob mob, Player attacker, int skillId, int level, int durationMs, DateTimeOffset now, List<MonsterStatusEffect> applied)
    {
        if (mob.Hp <= 1)
        {
            return;
        }

        var duration = durationMs >= 2_000_000_000 ? 5000 : Math.Max(0, durationMs);
        var damage = MonsterPoisonCalculator.PoisonDamage(mob.Stats.MaxHp, attacker, level, _random);
        TryApply(
            mob,
            new MonsterStatusEffect(MonsterStatus.Poison, damage, skillId, null, now.AddMilliseconds(duration), damage, attacker.Character.Id),
            applied);
    }

    private void TryApply(Mob mob, MonsterStatusEffect status, List<MonsterStatusEffect> applied)
    {
        var element = _skills.GetSkill(status.SkillId)?.Element;
        if (mob.TryApplyStatus(status, sourceElement: element) == MonsterStatusApplyResult.Applied)
        {
            applied.Add(status);
        }
    }

    private int? ChargeSource(Player player)
        => player.ActiveBuffs.FirstOrDefault(static b => b.Stat == MapleBuffStat.WK_CHARGE)?.SourceId;

    /// <summary>Java <c>getStatForBuff(stat)</c>。</summary>
    private MapleStatEffect? BuffEffect(Player player, MapleBuffStat stat)
        => player.ActiveBuffs.FirstOrDefault(b => b.Stat == stat) is { } buff ? Effect(buff.SourceId, buff.SkillLevel) : null;

    private MapleStatEffect? Effect(int skillId, int level)
        => level > 0 ? _skills.GetSkill(skillId)?.GetEffect(level) : null;

    private bool MakeChance(int prop) => prop == 100 || _random.Next(99) < prop;

    /// <summary>Java applyAttack switch 的盜賊技能組（鬥氣減速、BODY_PRESSURE、飛毒殺）。</summary>
    private static bool IsThiefChargeSkill(int skillId)
        => skillId is 4001344 or 4001002 or 4001334 or 4111005 or 4121007 or 4201005 or 4211002 or 4211004
            or 4221007 or 4221001 or 14001002 or 14001004 or 14111002 or 14111005;

    /// <summary>Java applyAttack switch 的狂狼勇士技能組。</summary>
    private static bool IsAranChargeSkill(int skillId)
        => skillId is 21000002 or 21100001 or 21100002 or 21100004 or 21110002 or 21110003 or 21110004 or 21110006
            or 21110007 or 21110008 or 21120002 or 21120005 or 21120006 or 21120009 or 21120010;

    /// <summary>Java <c>GameConstants.getStatFromWeapon</c>。</summary>
    public static MonsterStatus? StatFromWeapon(int itemId) => itemId switch
    {
        1302109 or 1312041 or 1322067 or 1332083 or 1372048 or 1382064 or 1402055 or 1412037 or 1422041 or 1432052
            or 1442073 or 1452064 or 1462058 or 1472079 or 1482035 => MonsterStatus.Acc,
        1302108 or 1312040 or 1322066 or 1332082 or 1372047 or 1382063 or 1402054 or 1412036 or 1422040 or 1432051
            or 1442072 or 1452063 or 1462057 or 1472078 or 1482036 => MonsterStatus.Speed,
        _ => null,
    };
}
