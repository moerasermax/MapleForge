using Maple.Application.Combat;
using Maple.Application.Mobs;
using Maple.Application.Parties;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Skills;

/// <summary>P145：一位隊友（或 GM buff 對象）被套用的結果（呼叫端送屬性、buff 與特效封包）。</summary>
public sealed record PartyBuffApplication(
    Player Target,
    short HpBefore,
    IReadOnlyList<PlayerStatUpdate> StatUpdates,
    PlayerBuffChange? Buff,
    IReadOnlyList<PlayerDisease> CuredDiseases,
    IReadOnlyList<int> ResetCooldownSkillIds);

/// <summary>
/// P145：對照 Java <c>MapleStatEffect.applyBuff</c> 的隊伍分支：<c>isPartyBuff() &amp;&amp; (有隊伍 || isGmBuff())</c> →
/// 以施放者位置與面向算 <c>calculateBoundingBox</c>，範圍內（<c>Rectangle.contains</c>）的其他玩家若
/// （GM buff 或同隊）且（復活技能對死者／其他技能對活人）就 <c>applyTo(applyfrom, affected, primary=false)</c>；
/// 時空置換另外清除其冷卻。P204：靈魂之石（22181003）走隨機分配分支。呼叫端持有 field 鎖。
/// </summary>
public sealed class PartyBuffService
{
    private readonly IPartyRegistry? _parties;
    private readonly Random _random;
    private readonly PlayerLocalStatsCalculator? _localStats;

    public PartyBuffService(IPartyRegistry? parties = null, Random? random = null, PlayerLocalStatsCalculator? localStats = null)
    {
        _parties = parties;
        _random = random ?? Random.Shared;
        _localStats = localStats;
    }

    /// <summary>Java <c>isPartyBuff</c>：有 lt/rb（partyBuff 恆為 true）且不是鬥氣系（1211003~1211008、1221003/1221004、11111007、12101005、4311001）。</summary>
    public static bool IsPartyBuff(MapleStatEffect effect)
        => effect.Area is not null
            && effect.SourceId is not (1211003 or 1211004 or 1211005 or 1211006 or 1211007 or 1211008
                or 1221003 or 1221004 or 11111007 or 12101005 or 4311001);

    /// <summary>Java <c>isGmBuff</c>：英雄的回響（1005、10001005、20001005）與 GM 技能 9001000~9001003、9001005、9001008。</summary>
    public static bool IsGmBuff(int skillId)
        => skillId is 1005 or 10001005 or 20001005 or 9001000 or 9001001 or 9001002 or 9001003 or 9001005 or 9001008;

    /// <summary>Java <c>isSoulStone</c>。</summary>
    public static bool IsSoulStone(MapleStatEffect effect) => effect.IsSkill && effect.SourceId == 22181003;

    /// <summary>Java <c>isResurrection</c>。</summary>
    public static bool IsResurrection(int skillId) => skillId is 9001005 or 2321006;

    /// <summary>Java applyTo 的 primary 區塊：<c>(overTime || isHeal()) &amp;&amp; !isEnergyCharge()</c> 才呼叫 applyBuff。</summary>
    public static bool ShouldApplyBuff(MapleStatEffect effect)
        => (effect.IsOverTime || (effect.IsSkill && effect.SourceId is 2301002 or 9101000 or 9001000))
            && !(effect.IsSkill && effect.SourceId is 5110001 or 15100004);

    public IReadOnlyList<PartyBuffApplication> Apply(
        FieldInstance field,
        Player caster,
        MapleStatEffect effect,
        bool facingLeft,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(effect);

        var gmBuff = IsGmBuff(effect.SourceId);
        var party = _parties?.GetPartyForCharacter(caster.Character.Id);
        if (ShouldApplyBuff(effect) && IsSoulStone(effect))
        {
            return party is null ? [] : ApplySoulStone(field, caster, effect, party.Id, now);
        }

        if (!ShouldApplyBuff(effect) || !IsPartyBuff(effect) || (party is null && !gmBuff) || effect.Area is not { } area)
        {
            return [];
        }

        var box = MobSkillService.BoundingBox(area, caster.Position, facingLeft);
        var resurrection = IsResurrection(effect.SourceId);
        var applied = new List<PartyBuffApplication>();
        foreach (var target in field.Players.ToArray())
        {
            if (ReferenceEquals(target, caster) || !MobSkillService.Contains(box, target.Position))
            {
                continue;
            }

            if (!gmBuff && _parties?.GetPartyForCharacter(target.Character.Id)?.Id != party!.Id)
            {
                continue;
            }

            var reset = effect.SourceId == SkillService.TimeLeapSkillId
                ? target.ResetSkillCooldownsExcept(SkillService.TimeLeapSkillId)
                : [];
            if (resurrection == target.IsAlive)
            {
                if (reset.Count > 0)
                {
                    applied.Add(new PartyBuffApplication(target, target.Hp, [], null, [], reset));
                }

                continue;
            }

            applied.Add(ApplyTo(caster, target, effect, resurrection, reset, now));
        }

        return applied;
    }

    /// <summary>
    /// P204：Java applyBuff 的靈魂之石分支——數地圖上同隊的活人 membrs，反覆掃描地圖（含施放者本人）
    /// 每人 <c>nextInt(y) == 0</c> 中選，直到選滿 <c>min(membrs, y)</c> 人，各自 <c>applyTo(primary=false)</c>
    /// （呼叫端送 showOwnBuffEffect(sid, 2) 與他人 showBuffeffect）。y ≤ 0 時 Java nextInt 會丟例外，這裡視為不分配。
    /// </summary>
    private IReadOnlyList<PartyBuffApplication> ApplySoulStone(FieldInstance field, Player caster, MapleStatEffect effect, int partyId, DateTimeOffset now)
    {
        var members = field.Players
            .Where(p => p.IsAlive && _parties?.GetPartyForCharacter(p.Character.Id)?.Id == partyId)
            .ToArray();
        var target = Math.Min(members.Length, effect.Y);
        if (target <= 0)
        {
            return [];
        }

        var awarded = new List<Player>(target);
        while (awarded.Count < target)
        {
            foreach (var member in members)
            {
                if (!awarded.Contains(member) && _random.Next(effect.Y) == 0)
                {
                    awarded.Add(member);
                }
            }
        }

        return awarded.Select(member => ApplyTo(caster, member, effect, resurrection: false, [], now)).ToArray();
    }

    /// <summary>
    /// Java <c>applyTo(applyfrom, applyto, primary=false)</c>：<c>calcHPChange/calcMPChange</c> 非 primary——不扣消耗，
    /// <c>hpR</c>/<c>mpR</c> 取「施放者」的目前最大 HP/MP（Java 用 applyfrom，照抄），<c>mp</c> 原值；
    /// 復活 → hpchange = 目標基礎最大 HP；驅散擲機率 → 解除全部異常；否則英雄意志 → 解除魅惑。
    /// 屬性更新 MP 有變才帶、HP 一律帶；buff 以原始持續時間登記（非 primary 無鍊金術延長）。
    /// </summary>
    private PartyBuffApplication ApplyTo(
        Player caster,
        Player target,
        MapleStatEffect effect,
        bool resurrection,
        IReadOnlyList<int> reset,
        DateTimeOffset now)
    {
        var hpBefore = target.Hp;
        var stats = target.Character.Stats;
        // P146：非 primary 的 calcHPChange（治癒術的 makeHealHP 用施放者總魔攻）；復活覆寫為目標基礎最大 HP。
        var hpChange = resurrection
            ? stats.MaxHp
            : SkillHpFormula.CalcHpChange(
                caster,
                effect,
                primary: false,
                SkillHpFormula.NeedsLocalStats(effect) ? _localStats?.Calculate(caster) : null,
                _random);

        int mpChange = effect.Mp;
        if (effect.MpRate != 0)
        {
            mpChange += (int)(caster.MaxMp * effect.MpRate);
        }

        IReadOnlyList<PlayerDisease> cured = [];
        if (SkillService.IsDispelSkill(effect.SourceId) && (effect.Prop == 100 || _random.Next(99) < effect.Prop))
        {
            cured = target.DispelDiseases();
        }
        else if (SkillService.IsHeroWillSkill(effect.SourceId) && target.DispelDisease(PlayerDisease.Seduce))
        {
            cured = [PlayerDisease.Seduce];
        }

        var updates = new List<PlayerStatUpdate>(2);
        if (hpChange != 0)
        {
            stats.Hp = (short)Math.Clamp(stats.Hp + hpChange, 0, target.MaxHp);
        }

        if (mpChange != 0)
        {
            stats.Mp = (short)Math.Clamp(stats.Mp + mpChange, 0, target.MaxMp);
            updates.Add(new PlayerStatUpdate(PlayerStatKind.Mp, stats.Mp));
        }

        updates.Add(new PlayerStatUpdate(PlayerStatKind.Hp, stats.Hp));
        return new PartyBuffApplication(target, hpBefore, updates, target.ApplyBuffStats(effect, now), cured, reset);
    }
}
