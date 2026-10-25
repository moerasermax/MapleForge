using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Skills;

/// <summary>
/// P133/P134：技能 <c>effect.applyTo</c> 開頭的 HP/MP 結算（攻擊尾段與 SPECIAL_MOVE 共用）。
/// <paramref name="Status"/> 非 Success 代表 Java applyTo 提前 return false（HP 或 MP 不足；不送封包、後續副作用不發生）。
/// 成功時 <paramref name="Updates"/> 依 Java <c>hpmpupdate</c>：MP 有變才帶、HP 一律帶，呼叫端以
/// <c>updatePlayerStats(..., true)</c> 送出。
/// </summary>
public sealed record SkillHpMpResult(SkillCastStatus Status, IReadOnlyList<PlayerStatUpdate> Updates)
{
    public bool Applied => Status == SkillCastStatus.Success;
}

/// <summary>P159：攻擊尾段 applyTo 登記的 buff；<paramref name="HomingMobObjectId"/> 非 null 時呼叫端改送 giveHoming。</summary>
public sealed record AttackSkillBuffResult(MapleStatEffect Effect, PlayerBuffChange Buff, int? HomingMobObjectId);

public sealed partial class SkillService
{
    public const int MpRecoverySkillId = 5101005;

    /// <summary>
    /// 對照 Java <c>DamageParse.applyAttack:533</c>（近戰／遠程）：<c>attack.skill != 0 &amp;&amp; (targets &gt; 0 || skill 不是
    /// 4331003/4341002) &amp;&amp; skill 不在 {21101003, 5110001, 15100004, 11101002, 13101002}</c> 才 applyTo；
    /// <c>applyAttackMagic:768</c>（魔法）：skill 不是群體治癒 2301002。不呼叫 applyTo 時回 null。
    /// </summary>
    public SkillHpMpResult? ApplyAttackSkillCost(Player player, int skillId, int targetCount, bool isMagic)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!AppliesAttackEffect(skillId, targetCount, isMagic) || GetAttackEffect(player, skillId) is not { } effect)
        {
            return null;
        }

        return ApplySkillHpMp(player, effect);
    }

    /// <summary>
    /// P159：攻擊尾段 applyTo（HP/MP 結算成功後）的 <c>if (overTime &amp;&amp; !isEnergyCharge()) applyBuffEffect</c>：
    /// 一般技能照常登記 buff（呼叫端送 giveBuff＋giveForeignBuff）；指定攻擊 5211006／精準砲擊 5220011 只在
    /// <c>linkMid &gt; 0</c> 時登記並改送 cancelHoming＋giveHoming，否則 Java 直接 return（不登記）。
    /// 能量獲得 5110001／15100004 已被 <see cref="AppliesAttackEffect"/> 排除。隊伍 applyBuff（攻擊技能無 lt/rb 隊伍 buff）未接。
    /// </summary>
    public AttackSkillBuffResult? ApplyAttackSkillBuff(Player player, int skillId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (GetAttackEffect(player, skillId) is not { IsOverTime: true } effect)
        {
            return null;
        }

        if (IsHomingSkill(effect.SourceId))
        {
            var mobObjectId = player.LinkMobObjectId;
            return mobObjectId > 0 && player.ApplyBuffStats(effect, now) is { } homing
                ? new AttackSkillBuffResult(effect, homing, mobObjectId)
                : null;
        }

        return player.ApplyBuffStats(effect, now, GetMorphStatups(effect, player.Character.Gender)) is { } buff
            ? new AttackSkillBuffResult(effect, buff, null)
            : null;
    }

    /// <summary>Java applyBuffEffect 的 <c>case 5211006: case 5220011</c>（Homing Beacon／Bullseye）。</summary>
    public static bool IsHomingSkill(int skillId) => skillId is 5211006 or 5220011;

    public static bool AppliesAttackEffect(int skillId, int targetCount, bool isMagic)
        => isMagic
            ? skillId != 2301002
            : skillId != 0
              && (targetCount > 0 || skillId is not (4331003 or 4341002))
              && skillId is not (21101003 or 5110001 or 15100004 or 11101002 or 13101002);

    /// <summary>
    /// Java <c>applyTo</c> 開頭（primary）：<c>calcHPChange</c>／<c>calcMPChange</c> →（P134）能量恢復 5101005 →
    /// HP 不足（<c>-hpchange &gt; hp</c> 且非殭屍）return false → 設 HP → MP 不足（<c>mp + mpchange &lt; 0</c>）return false
    /// → 設 MP。Java 在 MP 檢查失敗前已改 HP（照抄）。
    /// 偏差：Java 允許 <c>-hpchange == hp</c> 把 HP 扣到 0（伺服器不走死亡流程），MapleForge 視為不足；殭屍狀態下最低留 1。
    /// </summary>
    public SkillHpMpResult ApplySkillHpMp(Player player, MapleStatEffect effect)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(effect);

        var hpChange = CalcHpChange(player, effect);
        var mpChange = CalcMpChange(player, effect);
        var stats = player.Character.Stats;
        var zombify = player.HasDisease(PlayerDisease.Zombify);

        // Java else-if 鏈的 isMPRecovery()（驅散／英雄意志／cureDebuffs 分支都不是 5101005）：
        // toDecreaseHP = (getMaxHp() / 100) * 10（基礎最大 HP）；HP 足夠 → 扣 HP、回 (toDecreaseHP / 100) * y MP；
        // 否則 Java 寫成 hpchange = hp == 1 ? 0 : hp - 1（正值，等於回血；原碼疏失，照抄）。
        if (effect.IsSkill && effect.SourceId == MpRecoverySkillId)
        {
            var toDecreaseHp = stats.MaxHp / 100 * 10;
            if (stats.Hp > toDecreaseHp)
            {
                hpChange += -toDecreaseHp;
                mpChange += toDecreaseHp / 100 * effect.Y;
            }
            else
            {
                hpChange = stats.Hp == 1 ? 0 : stats.Hp - 1;
            }
        }

        if (hpChange != 0)
        {
            if (hpChange < 0 && -hpChange >= stats.Hp && !zombify)
            {
                return new SkillHpMpResult(SkillCastStatus.NotEnoughHp, []);
            }

            stats.Hp = (short)Math.Clamp(stats.Hp + hpChange, 1, player.MaxHp);
        }

        var updates = new List<PlayerStatUpdate>(2);
        if (mpChange != 0)
        {
            if (stats.Mp + mpChange < 0)
            {
                return new SkillHpMpResult(SkillCastStatus.NotEnoughMp, []);
            }

            stats.Mp = (short)Math.Min(player.MaxMp, stats.Mp + mpChange);
            updates.Add(new PlayerStatUpdate(PlayerStatKind.Mp, stats.Mp));
        }

        updates.Add(new PlayerStatUpdate(PlayerStatKind.Hp, stats.Hp));
        return new SkillHpMpResult(SkillCastStatus.Success, updates);
    }

    /// <summary>P146：Java <c>calcHPChange(applyfrom, primary=true)</c>，見 <see cref="SkillHpFormula"/>。</summary>
    private int CalcHpChange(Player player, MapleStatEffect effect)
        => SkillHpFormula.CalcHpChange(
            player,
            effect,
            primary: true,
            SkillHpFormula.NeedsLocalStats(effect) ? new Maple.Application.Combat.PlayerLocalStatsCalculator(_skills).Calculate(player) : null,
            _random);

    /// <summary>
    /// Java <c>calcMPChange(applyfrom, primary=true)</c>：<c>mp</c>（技能不經鍊金術）＋<c>(int)(currentMaxMp * mpR)</c>；
    /// mpCon：元素增幅（火毒 211/212→2110001、冰雷 221/222→2210001、炎術士 1211/1212→12110001）倍率 x/100、
    /// 集中術 CONCENTRATE 的技能 x 為減免百分比（潛能 mpconReduce 未移植，視為 0）、魔力無限 INFINITY → 整個 mpchange 歸 0。
    /// <c>mpchange -= (mpCon - mpCon * percent / 100) * mod</c> 為 int 複合指定（截斷）。
    /// </summary>
    private int CalcMpChange(Player player, MapleStatEffect effect)
    {
        int mpChange = effect.Mp;
        if (effect.MpRate != 0)
        {
            mpChange += (int)(player.MaxMp * effect.MpRate);
        }

        if (effect.MpCon == 0)
        {
            return mpChange;
        }

        var mod = 1.0;
        if (GetElementalAmpSkillId(player.Character.Job) is { } ampId
            && player.GetSkillLevel(ampId) is var ampLevel and > 0
            && _skills.GetSkill(ampId)?.GetEffect(ampLevel) is { } amp)
        {
            mod = amp.X / 100.0;
        }

        if (player.GetBuffValue(MapleBuffStat.INFINITY) is not null)
        {
            return 0;
        }

        var percentOff = GetBuffedSkillX(player, MapleBuffStat.CONCENTRATE) ?? 0;
        return (int)(mpChange - (effect.MpCon - effect.MpCon * percentOff / 100) * mod);
    }

    /// <summary>Java <c>getElementalAmp(job)</c>。</summary>
    private static int? GetElementalAmpSkillId(int job) => job switch
    {
        211 or 212 => 2110001,
        221 or 222 => 2210001,
        1211 or 1212 => 12110001,
        _ => null,
    };

    /// <summary>Java <c>getBuffedSkill_X(stat)</c>：該 buff 來源技能效果的 x（查不到技能時退回 buff 值）。</summary>
    private int? GetBuffedSkillX(Player player, MapleBuffStat stat)
    {
        var buff = player.ActiveBuffs.FirstOrDefault(b => b.Stat == stat);
        if (buff is null)
        {
            return null;
        }

        return _skills.GetSkill(buff.SourceId)?.GetEffect(buff.SkillLevel)?.X ?? buff.Value;
    }
}
