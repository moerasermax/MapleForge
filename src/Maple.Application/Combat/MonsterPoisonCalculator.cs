using Maple.Core.World;

namespace Maple.Application.Combat;

/// <summary>
/// P113：怪物持續傷害數值（對照 Java <c>MapleMonster.applyStatus</c> 的 poison／shadow web 分支）。
/// 純函式；亂數由呼叫端注入。
/// </summary>
public static class MonsterPoisonCalculator
{
    /// <summary>
    /// Java：<c>Attackstat = (盜賊 LUK : 法師 INT : 弓箭手或海盜 DEX : INT) / 20000</c>；
    /// <c>min(32767, maxHp * ((0.025 + skillLevel/1000) + Attackstat))</c>，≤ 1 時再加 <c>rand(10, maxHp)</c>。
    /// 總屬性取 <see cref="PlayerLocalStatsCalculator.PrimaryStats"/>（P136：基礎＋裝備＋楓葉祝福）。
    /// </summary>
    public static int PoisonDamage(long mobMaxHp, Player from, int skillLevel, Random random)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(random);

        var job = from.Character.Job;
        var (_, dex, intel, luk) = PlayerLocalStatsCalculator.PrimaryStats(from);
        var stat = IsThief(job) ? luk
            : IsMage(job) ? intel
            : IsBowman(job) || IsPirate(job) ? dex
            : intel;
        var attackStat = stat / 20000.0;
        var raw = Math.Min(short.MaxValue, mobMaxHp * ((0.025 + skillLevel / 1000.0) + attackStat));
        var damage = (int)raw;
        if (damage <= 1)
        {
            // Java Randomizer.rand(10, maxHp) = (int)(nextDouble() * (ub - lb + 1) + lb)。
            damage += (int)(random.NextDouble() * ((int)mobMaxHp - 10 + 1) + 10);
        }

        return damage;
    }

    /// <summary>Java 影網（4111003／14111001）：<c>(int)(maxHp / 50.0 + 0.999)</c>。</summary>
    public static int ShadowWebValue(long mobMaxHp) => (int)(mobMaxHp / 50.0 + 0.999);

    // Java MapleCharacter.isThief/isMage/isBowman/isPirate 的職業區間（與 MapleJobFamily 的分支判斷不同，照 Java）。
    private static bool IsThief(int job) => job is >= 400 and <= 422 or >= 1400 and <= 1412;

    private static bool IsMage(int job) => job is >= 200 and <= 232 or >= 1200 and <= 1212;

    private static bool IsBowman(int job) => job is >= 300 and <= 322 or >= 1300 and <= 1312;

    private static bool IsPirate(int job) => job is >= 500 and <= 522 or >= 1500 and <= 1512;
}
