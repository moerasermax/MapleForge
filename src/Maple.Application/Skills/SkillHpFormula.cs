using Maple.Application.Combat;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Skills;

/// <summary>
/// P146：Java <c>MapleStatEffect.calcHPChange(applyfrom, primary)</c>（施放者本人 primary=true 與隊友 primary=false 共用）。
/// </summary>
public static class SkillHpFormula
{
    public const int ChakraSkillId = 4211001;

    /// <summary>
    /// GM 技能 9001000/9101000 +500000；<c>hp</c>：技能 → <c>makeHealHP(hp / 100.0, 施放者總魔攻, 3, 5)</c>（殭屍時反號），
    /// 非技能 → 原值（殭屍減半）；<c>hpR</c> → <c>(int)(施放者目前最大 HP * hpR) / (殭屍 ? 2 : 1)</c>；primary 扣 hpCon；
    /// 氣絕 4211001 整個覆寫為 <c>(int)((rand(1,100) + 100) * 基礎 LUK * 0.033 + 基礎 DEX) * (y + 100) * 0.002)</c>
    /// ＋ <c>makeHealHP(y / 100.0, 總 LUK, 2.3, 3.5)</c>（覆寫在 hpCon 之後，照抄）。
    /// <paramref name="fromStats"/> 為施放者總屬性（缺省時以基礎 INT／LUK 近似）。
    /// </summary>
    public static int CalcHpChange(Player from, MapleStatEffect effect, bool primary, PlayerLocalStats? fromStats, Random random)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(random);

        var zombify = from.HasDisease(PlayerDisease.Zombify);
        var hpChange = effect.SourceId is 9001000 or 9101000 ? 500_000 : 0;
        if (effect.Hp != 0)
        {
            if (effect.IsSkill)
            {
                hpChange += MakeHealHp(effect.Hp / 100.0, fromStats?.Magic ?? from.Character.Stats.Int, 3, 5, random);
                if (zombify)
                {
                    hpChange = -hpChange;
                }
            }
            else
            {
                hpChange += effect.Hp;
                if (zombify)
                {
                    hpChange /= 2;
                }
            }
        }

        if (effect.HpRate != 0)
        {
            hpChange += (int)(from.MaxHp * effect.HpRate) / (zombify ? 2 : 1);
        }

        if (primary)
        {
            hpChange -= effect.HpCon;
        }

        if (effect.SourceId == ChakraSkillId)
        {
            var stats = from.Character.Stats;
            var v42 = effect.Y + 100;
            var v38 = (int)(random.NextDouble() * 100 + 1) + 100;
            hpChange = (int)((v38 * stats.Luk * 0.033 + stats.Dex) * v42 * 0.002);
            hpChange += MakeHealHp(effect.Y / 100.0, fromStats?.Luk ?? stats.Luk, 2.3, 3.5, random);
        }

        return hpChange;
    }

    /// <summary>是否需要施放者總屬性（技能 hp 治癒或氣絕）。</summary>
    public static bool NeedsLocalStats(MapleStatEffect effect)
        => (effect.Hp != 0 && effect.IsSkill) || effect.SourceId == ChakraSkillId;

    /// <summary>Java <c>makeHealHP</c>：<c>(int)(random * ((int)(stat*upper*rate) - (int)(stat*lower*rate) + 1)) + (int)(stat*lower*rate)</c>。</summary>
    public static int MakeHealHp(double rate, double stat, double lowerFactor, double upperFactor, Random random)
    {
        var lower = (int)(stat * lowerFactor * rate);
        var upper = (int)(stat * upperFactor * rate);
        return (int)(random.NextDouble() * (upper - lower + 1)) + lower;
    }
}
