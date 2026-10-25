using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Skills;

/// <summary>
/// P133：攻擊技能尾段 <c>effect.applyTo(player)</c> 的 HP/MP 結算。<paramref name="Applied"/> = false 代表 Java applyTo
/// 提前 return false（HP 或 MP 不足；不送封包、後續霧等副作用也不發生）。成功時 <paramref name="Updates"/> 依 Java
/// <c>hpmpupdate</c>：MP 有變才帶、HP 一律帶，呼叫端以 <c>updatePlayerStats(..., true)</c> 送出。
/// </summary>
public sealed record AttackSkillCostResult(bool Applied, IReadOnlyList<PlayerStatUpdate> Updates);

public sealed partial class SkillService
{
    /// <summary>
    /// 對照 Java <c>DamageParse.applyAttack:533</c>（近戰／遠程）：<c>attack.skill != 0 &amp;&amp; (targets &gt; 0 || skill 不是
    /// 4331003/4341002) &amp;&amp; skill 不在 {21101003, 5110001, 15100004, 11101002, 13101002}</c> 才 applyTo；
    /// <c>applyAttackMagic:768</c>（魔法）：skill 不是群體治癒 2301002。不呼叫 applyTo 時回 null。
    /// </summary>
    public AttackSkillCostResult? ApplyAttackSkillCost(Player player, int skillId, int targetCount, bool isMagic)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!AppliesAttackEffect(skillId, targetCount, isMagic) || GetAttackEffect(player, skillId) is not { } effect)
        {
            return null;
        }

        return ApplyHpMpChange(player, effect);
    }

    public static bool AppliesAttackEffect(int skillId, int targetCount, bool isMagic)
        => isMagic
            ? skillId != 2301002
            : skillId != 0
              && (targetCount > 0 || skillId is not (4331003 or 4341002))
              && skillId is not (21101003 or 5110001 or 15100004 or 11101002 or 13101002);

    /// <summary>
    /// Java <c>applyTo</c> 開頭（primary）：<c>calcHPChange</c>／<c>calcMPChange</c> → HP 不足（<c>-hpchange &gt; hp</c> 且非殭屍）
    /// return false → 設 HP → MP 不足（<c>mp + mpchange &lt; 0</c>）return false → 設 MP。Java 在 MP 檢查失敗前已改 HP（照抄）。
    /// 偏差：Java 允許 <c>-hpchange == hp</c> 把 HP 扣到 0（伺服器不走死亡流程），MapleForge 視為不足；殭屍狀態下最低留 1。
    /// </summary>
    private AttackSkillCostResult ApplyHpMpChange(Player player, MapleStatEffect effect)
    {
        var hpChange = CalcHpChange(player, effect);
        var mpChange = CalcMpChange(player, effect);
        var stats = player.Character.Stats;
        var zombify = player.HasDisease(PlayerDisease.Zombify);

        if (hpChange != 0)
        {
            if (hpChange < 0 && -hpChange >= stats.Hp && !zombify)
            {
                return new AttackSkillCostResult(false, []);
            }

            stats.Hp = (short)Math.Clamp(stats.Hp + hpChange, 1, player.MaxHp);
        }

        var updates = new List<PlayerStatUpdate>(2);
        if (mpChange != 0)
        {
            if (stats.Mp + mpChange < 0)
            {
                return new AttackSkillCostResult(false, []);
            }

            stats.Mp = (short)Math.Min(player.MaxMp, stats.Mp + mpChange);
            updates.Add(new PlayerStatUpdate(PlayerStatKind.Mp, stats.Mp));
        }

        updates.Add(new PlayerStatUpdate(PlayerStatKind.Hp, stats.Hp));
        return new AttackSkillCostResult(true, updates);
    }

    /// <summary>
    /// Java <c>calcHPChange(applyfrom, primary=true)</c> 的攻擊技能部分：GM 技能 9001000/9101000 +500000、
    /// <c>hpR</c>（<c>(int)(currentMaxHp * hpR) / (殭屍 ? 2 : 1)</c>）、<c>-hpCon</c>。技能 <c>hp</c>（治癒公式
    /// makeHealHP 需總魔攻）與 4211001 氣絕（SPECIAL_MOVE）不在攻擊路徑，未納入。
    /// </summary>
    private static int CalcHpChange(Player player, MapleStatEffect effect)
    {
        var hpChange = effect.SourceId is 9001000 or 9101000 ? 500_000 : 0;
        if (effect.HpRate != 0)
        {
            hpChange += (int)(player.MaxHp * effect.HpRate) / (player.HasDisease(PlayerDisease.Zombify) ? 2 : 1);
        }

        return hpChange - effect.HpCon;
    }

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
