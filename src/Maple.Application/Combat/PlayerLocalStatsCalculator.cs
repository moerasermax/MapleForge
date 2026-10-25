using Maple.Core.Items;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Combat;

/// <summary>
/// P136：Java <c>PlayerStats.recalcLocalStats</c> 結果的子集（<c>getTotalStr/Dex/Int/Luk</c>、<c>getTotalWatk</c>、
/// <c>getTotalMagic</c>、speed/jump、<c>dam_r</c>/<c>bossdam_r</c>、<c>hands</c>、<c>getCurrentMaxBaseDamage</c>）。
/// </summary>
public sealed record PlayerLocalStats(
    int Str,
    int Dex,
    int Int,
    int Luk,
    int Watk,
    int Magic,
    int Speed,
    int Jump,
    double DamageRate,
    double BossDamageRate,
    int Hands,
    float MaxBaseDamage);

/// <summary>
/// P136：對照 Java <c>PlayerStats.recalcLocalStats</c>（隨需計算，不快取；Java 在裝備/buff 變動時重算）。
/// 已移植：基礎＋裝備（含魔攻 = INT + Σ(裝備魔攻 + 裝備 INT)）、子彈/飛鏢基礎攻擊、龍魔導士 22000000、魔攻上限 1999（buff 前）、
/// 楓葉祝福（只乘基礎屬性）、英雄之回聲、狂狼連擊、弩/弓/狂狼精通被動、元素增幅 dam_r、精靈的祝福、
/// WATK/MATK/SPEED/JUMP/DASH buff、疾風步/貓頭鷹/狂暴/黑暗力量/鬥氣/雷鳴 dam_r、移動 140／跳躍 123 上限、
/// <c>calculateMaxBaseDamage</c>。
/// 未移植（MapleForge 尚無資料）：潛能、套裝效果、裝備 HpR/MpR 百分比、爆擊/熟練度/射程被動（CalcPassive_*）、命中。
/// </summary>
public sealed class PlayerLocalStatsCalculator
{
    public const short WeaponSlot = -11;
    public const int MagicCap = 1999;

    private readonly ISkillCatalog _skills;

    public PlayerLocalStatsCalculator(ISkillCatalog skills)
    {
        _skills = skills;
    }

    public PlayerLocalStats Calculate(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var job = player.Character.Job;
        var (str, dex, intel, luk) = PrimaryStats(player, out var equipMagic);
        var magic = player.Character.Stats.Int + equipMagic;
        var watk = job == 500 || job is >= 520 and <= 522 ? 20
            : job == 400 || job is >= 410 and <= 412 or >= 1400 and <= 1412 ? 30
            : 0;
        int speed = 100, jump = 100;
        foreach (var equip in player.Character.Equips)
        {
            watk += equip.Watk;
            speed += equip.Speed;
            jump += equip.Jump;
        }

        magic += player.GetSkillLevel(22000000);
        magic = Math.Min(magic, MagicCap);
        // Java：楓葉祝福的 INT 增量也加進魔攻（magic += localint_ - before）。
        magic += intel - PrimaryStatsWithoutMapleWarrior(player).Int;

        var dam = 100.0;
        var bossDam = 100.0;
        if (player.GetBuffValue(MapleBuffStat.ECHO_OF_HERO) is { } echo)
        {
            var d = echo / 100.0;
            watk += (int)(watk * d);
            magic += (int)(magic * d);
        }

        if (player.GetBuffValue(MapleBuffStat.ARAN_COMBO) is { } combo)
        {
            watk += combo / 10;
        }

        switch (job)
        {
            case 322:
                watk += PassiveX(player, 3220004);
                break;
            case 312:
                watk += PassiveX(player, 3120005);
                break;
            case 211 or 212:
                ApplyAmplification(player, 2110001, ref dam, ref bossDam);
                break;
            case 221 or 222:
                ApplyAmplification(player, 2210001, ref dam, ref bossDam);
                break;
            case 1211 or 1212:
                ApplyAmplification(player, 12110001, ref dam, ref bossDam);
                break;
            case 2112:
                watk += PassiveX(player, 21120001);
                break;
        }

        if (Effect(BlessingOfFairySkillId(job), player.GetSkillLevel(BlessingOfFairySkillId(job))) is { } bof)
        {
            watk += bof.X;
            magic += bof.Y;
        }

        watk += player.GetBuffValue(MapleBuffStat.WATK) ?? 0;
        magic += player.GetBuffValue(MapleBuffStat.MATK) ?? 0;
        speed += (player.GetBuffValue(MapleBuffStat.SPEED) ?? 0) + (player.GetBuffValue(MapleBuffStat.DASH_SPEED) ?? 0);
        jump += (player.GetBuffValue(MapleBuffStat.JUMP) ?? 0) + (player.GetBuffValue(MapleBuffStat.DASH_JUMP) ?? 0);

        MultiplyByBuffDamage(player, MapleBuffStat.WIND_WALK, ref dam, ref bossDam);
        if (BuffEffect(player, MapleBuffStat.OWL_SPIRIT) is { } owl)
        {
            dam *= owl.Y / 100.0;
            bossDam *= owl.Y / 100.0;
        }

        if (player.GetBuffValue(MapleBuffStat.BERSERK_FURY) is not null)
        {
            dam *= 2.0;
            bossDam *= 2.0;
        }

        // Java：黑暗力量 1320006 只要學了就乘（不看 HP 門檻，照抄）。
        if (Effect(1320006, player.GetSkillLevel(1320006)) is { } berserk)
        {
            dam *= berserk.Damage / 100.0;
            bossDam *= berserk.Damage / 100.0;
        }

        MultiplyByBuffDamage(player, MapleBuffStat.WK_CHARGE, ref dam, ref bossDam);
        MultiplyByBuffDamage(player, MapleBuffStat.LIGHTNING_CHARGE, ref dam, ref bossDam);

        speed = Math.Min(speed, 140);
        jump = Math.Min(jump, 123);

        return new PlayerLocalStats(
            str,
            dex,
            intel,
            luk,
            watk,
            magic,
            speed,
            jump,
            dam,
            bossDam,
            dex + intel + luk,
            CalculateMaxBaseDamage(player, str, dex, intel, luk, magic, watk));
    }

    /// <summary>
    /// Java <c>localstr/localdex/localint_/localluk</c>：基礎＋裝備，再加楓葉祝福（<c>localX += (buff / 100.0) * 基礎X</c>，
    /// 複合指定截斷）。不需技能資料，供毒傷等只看總屬性的計算直接使用。
    /// </summary>
    public static (int Str, int Dex, int Int, int Luk) PrimaryStats(Player player)
        => PrimaryStats(player, out _);

    private static (int Str, int Dex, int Int, int Luk) PrimaryStats(Player player, out int equipMagic)
    {
        var (str, dex, intel, luk) = PrimaryStatsWithoutMapleWarrior(player, out equipMagic);
        if (player.GetBuffValue(MapleBuffStat.MAPLE_WARRIOR) is { } mapleWarrior)
        {
            var d = mapleWarrior / 100.0;
            var stats = player.Character.Stats;
            str = (int)(str + d * stats.Str);
            dex = (int)(dex + d * stats.Dex);
            luk = (int)(luk + d * stats.Luk);
            intel = (int)(intel + d * stats.Int);
        }

        return (str, dex, intel, luk);
    }

    private static (int Str, int Dex, int Int, int Luk) PrimaryStatsWithoutMapleWarrior(Player player)
        => PrimaryStatsWithoutMapleWarrior(player, out _);

    private static (int Str, int Dex, int Int, int Luk) PrimaryStatsWithoutMapleWarrior(Player player, out int equipMagic)
    {
        var stats = player.Character.Stats;
        int str = stats.Str, dex = stats.Dex, intel = stats.Int, luk = stats.Luk;
        equipMagic = 0;
        foreach (var equip in player.Character.Equips)
        {
            str += equip.Str;
            dex += equip.Dex;
            intel += equip.Int;
            luk += equip.Luk;
            equipMagic += equip.Matk + equip.Int;
        }

        return (str, dex, intel, luk);
    }

    /// <summary>Java <c>calculateMaxBaseDamage(magic, watk)</c>（含「矛」用 STR/DEX ×1.25 的原碼寫法）。</summary>
    private static float CalculateMaxBaseDamage(Player player, int str, int dex, int intel, int luk, int magic, int watk)
    {
        if (watk == 0)
        {
            return 1;
        }

        var job = player.Character.Job;
        var weapon = player.Character.Equips.FirstOrDefault(static e => e.Position == WeaponSlot) is { } item
            ? WeaponTypes.FromItemId(item.ItemId)
            : WeaponType.None;
        var magician = job is >= 200 and <= 232 or >= 1200 and <= 1212;
        var thief = job is >= 400 and <= 434 or >= 1400 and <= 1412;
        var (main, secondary) = weapon switch
        {
            WeaponType.Polearm => ((int)(str * 1.25), (int)(dex * 1.25)),
            WeaponType.Bow or WeaponType.Crossbow => (dex * 2, str * 2),
            WeaponType.Claw or WeaponType.Dagger => thief ? (luk, dex + str) : (str, dex),
            WeaponType.Knuckle => (str, dex),
            WeaponType.Gun => (dex, str),
            WeaponType.None => job is >= 500 and <= 522 or >= 1500 and <= 1512 ? (str, dex) : (0, 0),
            _ => magician ? (intel, luk) : (str, dex),
        };

        return (weapon.MaxDamageMultiplier() * main + secondary) * (magician ? magic : watk) / 100;
    }

    /// <summary>Java <c>GameConstants.getBofForJob</c>：冒險家 12、皇家騎士團 10000012、其他 20000012。</summary>
    private static int BlessingOfFairySkillId(int job) => job is >= 0 and < 1000 ? 12 : job is >= 1000 and < 2000 ? 10000012 : 20000012;

    private int PassiveX(Player player, int skillId)
        => player.GetSkillLevel(skillId) is var level and > 0 ? Effect(skillId, level)?.X ?? 0 : 0;

    private void ApplyAmplification(Player player, int skillId, ref double dam, ref double bossDam)
    {
        if (player.GetSkillLevel(skillId) is var level and > 0 && Effect(skillId, level) is { } amp)
        {
            dam *= amp.Y / 100.0;
            bossDam *= amp.Y / 100.0;
        }
    }

    private void MultiplyByBuffDamage(Player player, MapleBuffStat stat, ref double dam, ref double bossDam)
    {
        if (BuffEffect(player, stat) is { } effect)
        {
            dam *= effect.Damage / 100.0;
            bossDam *= effect.Damage / 100.0;
        }
    }

    /// <summary>Java <c>getStatForBuff(stat)</c>：該 buff 來源技能在施放等級的效果。</summary>
    private MapleStatEffect? BuffEffect(Player player, MapleBuffStat stat)
        => player.ActiveBuffs.FirstOrDefault(b => b.Stat == stat) is { } buff ? Effect(buff.SourceId, buff.SkillLevel) : null;

    private MapleStatEffect? Effect(int skillId, int level)
        => level > 0 ? _skills.GetSkill(skillId)?.GetEffect(level) : null;
}
