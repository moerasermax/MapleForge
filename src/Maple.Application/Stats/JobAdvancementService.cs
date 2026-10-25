using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Stats;

/// <summary>
/// P167：<c>MapleCharacter.changeJob</c> 的結果。呼叫端依 Java 順序送包：<see cref="ResetStats"/>（resetStatsByJob，
/// updatePlayerStats(false)）→ <see cref="SpAndJob"/>（updateSp、updateSingleStat(JOB)）→ <see cref="Vitals"/>
/// （MAXHP/MAXMP/HP/MP）→ 他人 showForeignEffect(8)、silentPartyUpdate、guildUpdate → <see cref="BaseSkills"/>。
/// </summary>
public sealed record JobChangeResult(
    bool AbnormalLevel,
    IReadOnlyList<PlayerStatUpdate> ResetStats,
    IReadOnlyList<PlayerStatUpdate> SpAndJob,
    IReadOnlyList<PlayerStatUpdate> Vitals,
    IReadOnlyList<(int SkillId, byte Level, byte MasterLevel)> BaseSkills);

/// <summary>P167：對照 Java <c>MapleCharacter.changeJob</c>／<c>resetStats</c>／<c>resetStatsByJob</c>／<c>baseSkills</c>。</summary>
public sealed class JobAdvancementService
{
    public const string AbnormalLevelMessage = "由於您的轉職等級非普通轉職等級，技能點異常後果自負。";

    private readonly ISkillCatalog? _skills;
    private readonly Random _random;

    public JobAdvancementService(ISkillCatalog? skills = null, Random? random = null)
    {
        _skills = skills;
        _random = random ?? Random.Shared;
    }

    /// <summary>Java <c>teachSkill(id, level)</c> 用的技能最大等級（查無技能回 0）。</summary>
    public int GetMaxLevel(int skillId) => _skills?.GetSkill(skillId)?.MaxLevel ?? 0;

    public JobChangeResult ChangeJob(Player player, int newJob)
    {
        ArgumentNullException.ThrowIfNull(player);

        var chr = player.Character;
        var abnormal = chr.Level is not (8 or 10 or 30 or 70 or 120);
        chr.Job = (short)newJob;
        if (newJob is not (0 or 1000 or 2000 or 2001))
        {
            chr.RemainingSp++;
            if (newJob % 10 >= 2)
            {
                chr.RemainingSp += 2;
            }
        }

        // Java：newJob > 0 && !isGM() → resetStatsByJob(true)：baseJob = job % 1000，只有剛好 100/200/300/400/500（一轉）才重設。
        IReadOnlyList<PlayerStatUpdate> reset = newJob > 0 ? ResetStatsByJob(player) : [];
        IReadOnlyList<PlayerStatUpdate> spAndJob =
        [
            new PlayerStatUpdate(PlayerStatKind.AvailableSp, chr.RemainingSp),
            new PlayerStatUpdate(PlayerStatKind.Job, chr.Job),
        ];

        int maxHp = chr.Stats.MaxHp, maxMp = chr.Stats.MaxMp;
        switch (newJob)
        {
            case 100 or 1100 or 2100:
                maxHp += Rand(200, 250);
                break;
            case 200:
                maxMp += Rand(100, 150);
                break;
            case 300 or 400 or 500:
                maxHp += Rand(100, 150);
                maxMp += Rand(25, 50);
                break;
            case 110 or 120 or 130 or 510 or 512 or 1110 or 2110:
                maxHp += Rand(300, 350);
                break;
            case 210 or 220 or 230:
                maxMp += Rand(400, 450);
                break;
            case 310 or 312 or 320 or 322 or 410 or 412 or 420 or 422 or 520 or 522 or 1310 or 1410:
                // Java 兩次都加在 maxhp（原碼如此，照抄）。
                maxHp += Rand(300, 350);
                maxHp += Rand(150, 200);
                break;
            case 900 or 800:
                maxHp += 30000;
                maxHp += 30000;
                break;
        }

        maxHp = Math.Min(maxHp, 30000);
        maxMp = Math.Min(maxMp, 30000);
        chr.Stats.MaxHp = (short)maxHp;
        chr.Stats.MaxMp = (short)maxMp;
        chr.Stats.Hp = (short)maxHp;
        chr.Stats.Mp = (short)maxMp;
        IReadOnlyList<PlayerStatUpdate> vitals =
        [
            new PlayerStatUpdate(PlayerStatKind.MaxHp, maxHp),
            new PlayerStatUpdate(PlayerStatKind.MaxMp, maxMp),
            new PlayerStatUpdate(PlayerStatKind.Hp, maxHp),
            new PlayerStatUpdate(PlayerStatKind.Mp, maxMp),
        ];

        return new JobChangeResult(abnormal, reset, spAndJob, vitals, BaseSkills(player));
    }

    /// <summary>
    /// Java <c>resetStats(str, dex, int, luk)</c>：total = 四素質 + 剩餘 AP，設定四素質，剩餘 AP = total − 四者；
    /// 更新 [STR, DEX, INT, LUK, AVAILABLEAP]。
    /// </summary>
    public static IReadOnlyList<PlayerStatUpdate> ResetStats(Player player, int str, int dex, int intel, int luk)
    {
        ArgumentNullException.ThrowIfNull(player);

        var chr = player.Character;
        var stats = chr.Stats;
        var total = stats.Str + stats.Dex + stats.Luk + stats.Int + chr.RemainingAp - str - dex - intel - luk;
        (stats.Str, stats.Dex, stats.Int, stats.Luk) = ((short)str, (short)dex, (short)intel, (short)luk);
        chr.RemainingAp = (short)total;
        return
        [
            new PlayerStatUpdate(PlayerStatKind.Str, str),
            new PlayerStatUpdate(PlayerStatKind.Dex, dex),
            new PlayerStatUpdate(PlayerStatKind.Int, intel),
            new PlayerStatUpdate(PlayerStatKind.Luk, luk),
            new PlayerStatUpdate(PlayerStatKind.AvailableAp, total),
        ];
    }

    private static IReadOnlyList<PlayerStatUpdate> ResetStatsByJob(Player player)
        => (player.Character.Job % 1000) switch
        {
            100 => ResetStats(player, 25, 4, 4, 4),
            200 => ResetStats(player, 4, 4, 20, 4),
            300 or 400 => ResetStats(player, 4, 25, 4, 4),
            500 => ResetStats(player, 4, 20, 4, 4),
            _ => [],
        };

    /// <summary>
    /// Java <c>baseSkills()</c>：<c>getJobNumber(job) ≥ 3</c>（三轉以上）時，該職業所有「非隱藏、四轉、未學且無 master、
    /// 技能 masterLevel &gt; 0」的技能 → changeSkillLevel(0, masterLevel)。
    /// </summary>
    private IReadOnlyList<(int SkillId, byte Level, byte MasterLevel)> BaseSkills(Player player)
    {
        var job = player.Character.Job;
        if (_skills is null || JobNumber(job) < 3)
        {
            return [];
        }

        var changes = new List<(int, byte, byte)>();
        foreach (var skillId in _skills.GetSkillIdsByJob(job))
        {
            if (_skills.GetSkill(skillId) is { IsInvisible: false, IsFourthJob: true, MasterLevel: > 0 } skill
                && player.GetSkillLevel(skillId) <= 0
                && player.GetMasterLevel(skillId) <= 0)
            {
                player.ChangeSkillLevel(skillId, 0, (byte)skill.MasterLevel);
                changes.Add((skillId, 0, (byte)skill.MasterLevel));
            }
        }

        return changes;
    }

    /// <summary>Java <c>GameConstants.getJobNumber</c>。</summary>
    public static int JobNumber(int job)
    {
        var j = job % 1000;
        return j / 100 == 0 ? 0 : j / 10 == 0 ? 1 : 2 + (j % 10);
    }

    private int Rand(int min, int max) => _random.Next(min, max + 1);
}
