namespace Maple.Core.Characters;

/// <summary>
/// 職業分支判斷（對照舊 Java <c>client.MapleJob</c>）。純函式、無狀態，只移植
/// <c>PartySearchJob.checkJob</c> 實際用到的子集（含私服自訂 Cygnus 五轉/Wild Hunter）。
/// </summary>
public static class MapleJobFamily
{
    /// <summary>對照 Java <c>MapleJob.is初心者</c>：職業是否屬於各職業線的初心者階段。</summary>
    public static bool IsBeginner(int job)
    {
        if (job <= 5000)
        {
            var skip = job != 5000
                && (job < 2001 || (job > 2005 && (job <= 3000 || (job > 3002 && (job <= 4000 || job > 4002)))));
            if (!skip)
            {
                return true;
            }
        }
        else if (job >= 6000 && (job <= 6001 || job == 13000))
        {
            return true;
        }

        var result = IsJob12000(job);
        return job % 1000 == 0 || job / 100 == 8000 || job == 8001 || result;
    }

    private static bool IsJob12000(int job) => IsJob12000LowLv(job) || IsJob12000HighLv(job);

    private static bool IsJob12000HighLv(int job) => job is 12003 or 12004;

    private static bool IsJob12000LowLv(int job) => job is 12000 or 12001 or 12002;

    /// <summary>對照 Java <c>MapleJob.getJobBranch</c>。</summary>
    public static int GetJobBranch(int job) => job / 100 == 27 ? 2 : job % 1000 / 100;

    public static bool IsWarrior(int job) => GetJobBranch(job) == 1;

    public static bool IsMagician(int job) => GetJobBranch(job) == 2;

    public static bool IsBowman(int job) => GetJobBranch(job) == 3;

    public static bool IsThief(int job) => GetJobBranch(job) is 4 or 6;

    public static bool IsPirate(int job) => GetJobBranch(job) is 5 or 6;

    public static bool IsHero(int job) => job / 10 == 11;

    public static bool IsPaladin(int job) => job / 10 == 12;

    public static bool IsDarkKnight(int job) => job / 10 == 13;

    public static bool IsFpArchMage(int job) => job / 10 == 21;

    public static bool IsIlArchMage(int job) => job / 10 == 22;

    public static bool IsBishop(int job) => job / 10 == 23;

    public static bool IsBowmaster(int job) => job / 10 == 31;

    public static bool IsMarksman(int job) => job / 10 == 32;

    public static bool IsNightLord(int job) => job / 10 == 41;

    public static bool IsShadower(int job) => job / 10 == 42;

    public static bool IsBuccaneer(int job) => job / 10 == 51;

    public static bool IsCorsair(int job) => job / 10 == 52;

    /// <summary>聖魂劍士（Dawn Warrior，Cygnus 五轉之一）。</summary>
    public static bool IsDawnWarrior(int job) => job / 100 == 11;

    /// <summary>烈焰巫師（Blaze Wizard）。</summary>
    public static bool IsBlazeWizard(int job) => job / 100 == 12;

    /// <summary>破風使者（Wind Archer）。</summary>
    public static bool IsWindArcher(int job) => job / 100 == 13;

    /// <summary>暗夜行者（Night Walker，Cygnus 版，非俠盜系 411/412）。</summary>
    public static bool IsNightWalker(int job) => job / 100 == 14;

    /// <summary>閃雷悍將（Thunder Breaker）。</summary>
    public static bool IsThunderBreaker(int job) => job / 100 == 15;

    /// <summary>狂狼勇士（Wild Hunter）。</summary>
    public static bool IsWildHunter(int job) => job / 100 == 21 || job == 2000;
}
