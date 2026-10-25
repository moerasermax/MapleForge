using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Skills;

public enum SkillCastStatus
{
    Success,
    Dead,
    UnknownSkill,
    SkillNotLearned,
    LevelMismatch,
    NoEffect,
    OnCooldown,
    NotEnoughHp,
    NotEnoughMp,
    /// <summary>未學／等級不符的武陵或金字塔技能在其專屬地圖外施放（P106）。</summary>
    OutsideEventMap,
}

public enum CancelBuffStatus
{
    Success,
    UnknownSkill,
    ChargeSkill,
    NoActiveBuff,
}

public enum AranComboStatus
{
    Success,
    NotAranJob,
    SkillLevelTooLow,
    UnknownSkill,
    NoEffect,
}

public sealed record SkillCastResult(
    SkillCastStatus Status,
    int SkillId,
    MapleSkill? Skill,
    MapleStatEffect? Effect,
    PlayerBuffChange? AppliedBuff,
    int CooldownStartedSeconds = 0,
    IReadOnlyList<int>? ResetCooldownSkillIds = null,
    // P108：未學武陵技能在道場施放時能量歸零（呼叫端送能量封包）。
    bool MulungEnergyReset = false,
    // P127：驅散／英雄意志解除的異常（呼叫端送 cancelDebuff / cancelForeignDebuff）。
    IReadOnlyList<PlayerDisease>? CuredDiseases = null);

/// <summary>P108：武陵技能攻擊前的能量判斷（對照 Java DamageParse.applyAttack／applyAttackMagic）。</summary>
public enum MulungAttackGate
{
    /// <summary>不是武陵技能。</summary>
    NotApplicable,
    /// <summary>可施放，能量已歸零（呼叫端送能量封包）。</summary>
    Consumed,
    /// <summary>能量不足或不在武陵道場：整次攻擊丟棄（Java return，不送封包）。</summary>
    Blocked,
}

public enum AttackCooldownStatus
{
    /// <summary>普攻、未知技能、未學技能、無效果或技能本身沒有冷卻：不影響攻擊。</summary>
    NotApplicable,
    /// <summary>技能冷卻中：整次攻擊應丟棄。</summary>
    OnCooldown,
    /// <summary>已登記冷卻，呼叫端應通知客戶端冷卻秒數。</summary>
    Started,
}

public sealed record AttackCooldownResult(AttackCooldownStatus Status, int Seconds);

public sealed record CancelBuffResult(
    CancelBuffStatus Status,
    int SourceId,
    MapleSkill? Skill,
    IReadOnlyList<PlayerBuffCancellation> Cancellations);

public sealed record AranComboResult(
    AranComboStatus Status,
    int Combo,
    int RequiredSkillLevel,
    MapleSkill? Skill,
    MapleStatEffect? Effect,
    PlayerBuffChange? AppliedBuff);

public sealed partial class SkillService
{
    /// <summary>槍神「海盜船」（Java <c>槍神.海盜船</c>）：施放時不登記冷卻。</summary>
    public const int CorsairBattleshipSkillId = 5221006;

    /// <summary>黑騎士「狂戰士之怒」被動（Java <c>SkillFactory.getSkill(1320006)</c>）。</summary>
    public const int BerserkSkillId = 1320006;

    /// <summary>拳霸「時間置換」（Java <c>isTimeLeap()</c>：<c>sourceid == 5121010</c>）。</summary>
    public const int TimeLeapSkillId = 5121010;
    private const int MulungDojoMapGroup = 92502;
    private const int PyramidMapGroup = 92602;

    private readonly ISkillCatalog _skills;
    private readonly Random _random;

    public SkillService(ISkillCatalog skills, Random? random = null)
    {
        _skills = skills;
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// P141：對照 Java <c>applyAttack</c>（物理攻擊，處理到第一隻射程內怪物時）：疾風步一律
    /// <c>cancelEffectFromBuffStat(WIND_WALK)</c>；隱身術除非來源是進階隱身術 4330001 且 <c>makeChanceResult</c> 成功，
    /// 否則取消（GM 豁免未移植）。回傳被取消的效果（呼叫端送 CANCEL_BUFF 並走取消副作用）。
    /// </summary>
    public IReadOnlyList<PlayerBuffCancellation> CancelBuffsOnAttack(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var cancelled = new List<PlayerBuffCancellation>();
        if (player.ActiveBuffs.FirstOrDefault(static b => b.Stat == MapleBuffStat.WIND_WALK) is { } windWalk)
        {
            cancelled.AddRange(player.CancelBuffBySource(windWalk.SourceId));
        }

        if (player.ActiveBuffs.FirstOrDefault(static b => b.Stat == MapleBuffStat.DARKSIGHT) is { } darkSight)
        {
            var keep = darkSight.SourceId == AdvancedDarkSightSkillId
                && _skills.GetSkill(darkSight.SourceId)?.GetEffect(darkSight.SkillLevel) is { } effect
                && (effect.Prop == 100 || _random.Next(99) < effect.Prop);
            if (!keep)
            {
                cancelled.AddRange(player.CancelBuffBySource(darkSight.SourceId));
            }
        }

        return cancelled;
    }

    public const int AdvancedDarkSightSkillId = 4330001;

    /// <summary>
    /// P139：Java <c>applyBuffEffect</c> 對同圖其他玩家廣播的 <c>giveForeignBuff</c> 狀態（施放者本人另收 giveBuff）：
    /// 隱身術系（9001004、13101006、4330001、4001003、14001003）DARKSIGHT 0、鬥氣集中（1111002、11111001）COMBO 0、
    /// 無形之箭（3101004、3201004、13101003）SOULARROW 0、影分身（4111002、14111000）SHADOWPARTNER 0、
    /// 飛翔（x001026）SOARING 1、狂暴之怒（x001011）或 berserk2 BERSERK_FURY 1、金剛不壞（x001010）DIVINE_BODY 1。
    /// 變身（MORPH，Java 改用 Selfstat 註冊）、海盜衝刺類（giveForeignPirate）、騎寵（showMonsterRiding）另議，回 null。
    /// </summary>
    public static IReadOnlyList<BuffStatValue>? GetForeignBuffStats(MapleStatEffect effect)
        => GetForeignBuffStatsCore(effect);

    /// <summary>Java <c>isPirateMorph</c>：15111002、5111005、5121003（技能）。</summary>
    public static bool IsPirateMorph(MapleStatEffect effect) => effect.IsSkill && effect.SourceId is 15111002 or 5111005 or 5121003;

    /// <summary>P156：Java <c>isMorph() || isPirateMorph()</c>（applyBuffEffect 改用 Selfstat 的條件）。</summary>
    public static bool IsMorphBuff(MapleStatEffect effect) => effect.MorphId > 0 || IsPirateMorph(effect);

    /// <summary>
    /// P156：Java <c>getMorph(chr)</c>：15111002／5111005 → 1000、5121003 → 1001、5101007 → 1002、13111005 → 1003，其他取 WZ morph；
    /// 1000／1001／1003 女性角色（gender 1）+100。
    /// </summary>
    public static int GetMorphValue(MapleStatEffect effect, int gender)
    {
        var morph = effect.SourceId switch
        {
            15111002 or 5111005 => 1000,
            5121003 => 1001,
            5101007 => 1002,
            13111005 => 1003,
            _ => effect.MorphId,
        };
        return morph is 1000 or 1001 or 1003 && gender == 1 ? morph + 100 : morph;
    }

    /// <summary>
    /// P156：Java applyBuffEffect 的變身 Selfstat：<c>[JUMP=jump, WDEF=wdef, MDEF=wdef（原碼如此）, SPEED=speed, MORPH=getMorph(chr)]</c>，
    /// 取代效果本身的 statups 註冊並送出；非變身回 null。
    /// </summary>
    public static IReadOnlyList<BuffStatValue>? GetMorphStatups(MapleStatEffect effect, int gender)
        => IsMorphBuff(effect)
            ?
            [
                new BuffStatValue(MapleBuffStat.JUMP, effect.Jump),
                new BuffStatValue(MapleBuffStat.WDEF, effect.Wdef),
                new BuffStatValue(MapleBuffStat.MDEF, effect.Wdef),
                new BuffStatValue(MapleBuffStat.SPEED, effect.Speed),
                new BuffStatValue(MapleBuffStat.MORPH, GetMorphValue(effect, gender)),
            ]
            : null;

    /// <summary>
    /// P147：Java <c>applyBuffEffect</c> 走 givePirate／giveForeignPirate 的技能：極速領域 5121009／15111005、
    /// 衝刺 5001005／15001003、龍捲風 4321000（僅技能）。
    /// </summary>
    public static bool IsPirateBuff(MapleStatEffect effect)
        => effect.IsSkill && effect.SourceId is 5121009 or 15111005 or 5001005 or 4321000 or 15001003;

    private static IReadOnlyList<BuffStatValue>? GetForeignBuffStatsCore(MapleStatEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (!effect.IsSkill)
        {
            return null;
        }

        return effect.SourceId switch
        {
            9001004 or 13101006 or 4330001 or 4001003 or 14001003 => [new BuffStatValue(MapleBuffStat.DARKSIGHT, 0)],
            1111002 or 11111001 => [new BuffStatValue(MapleBuffStat.COMBO, 0)],
            3101004 or 3201004 or 13101003 => [new BuffStatValue(MapleBuffStat.SOULARROW, 0)],
            4111002 or 14111000 => [new BuffStatValue(MapleBuffStat.SHADOWPARTNER, 0)],
            1026 or 10001026 or 20001026 or 20011026 or 30001026 => [new BuffStatValue(MapleBuffStat.SOARING, 1)],
            1011 or 10001011 or 20001011 or 20011011 or 30001011 => [new BuffStatValue(MapleBuffStat.BERSERK_FURY, 1)],
            1010 or 10001010 or 20001010 or 20011010 or 30001010 => [new BuffStatValue(MapleBuffStat.DIVINE_BODY, 1)],
            _ when effect.Statups.Any(static s => s.Stat == MapleBuffStat.BERSERK_FURY) => [new BuffStatValue(MapleBuffStat.BERSERK_FURY, 1)],
            _ => null,
        };
    }

    public SkillCastResult Cast(Player player, int skillId, int clientSkillLevel, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!player.IsAlive)
        {
            return new SkillCastResult(SkillCastStatus.Dead, skillId, null, null, null);
        }

        var skill = _skills.GetSkill(skillId);
        if (skill is null)
        {
            return new SkillCastResult(SkillCastStatus.UnknownSkill, skillId, null, null, null);
        }

        // P106：對照 Java PlayerHandler.SpecialMove——未學或等級不符時，只有武陵／金字塔技能可放行，
        // 但必須在其專屬地圖（武陵道場 92502xxxx、金字塔 92602xxxx）。
        var mulungEnergyReset = false;
        var serverLevel = player.GetSkillLevel(skillId);
        if (serverLevel <= 0 || serverLevel != clientSkillLevel)
        {
            if (!IsMulungSkill(skillId) && !IsPyramidSkill(skillId))
            {
                var rejected = serverLevel <= 0 ? SkillCastStatus.SkillNotLearned : SkillCastStatus.LevelMismatch;
                return new SkillCastResult(rejected, skillId, skill, null, null);
            }

            var requiredMapGroup = IsMulungSkill(skillId) ? MulungDojoMapGroup : PyramidMapGroup;
            if (player.Character.MapId / 10000 != requiredMapGroup)
            {
                return new SkillCastResult(SkillCastStatus.OutsideEventMap, skillId, skill, null, null);
            }

            // P108：對照 Java chr.mulungEnergyModify(false)——在道場施放武陵技能時能量歸零。
            if (IsMulungSkill(skillId))
            {
                player.ModifyMulungEnergy(increase: false);
                mulungEnergyReset = true;
            }
        }

        // P106：效果等級取 getLinkedSkill 對應後的技能等級（Java skill.getEffect(getSkillLevel(getLinkedSkill(id)))；
        // 等級 0 時 GetEffect 回第 1 級，與 Java Skill.getEffect 相同）。
        var effect = skill.GetEffect(player.GetSkillLevel(GetLinkedSkillId(skillId)));
        if (effect is null)
        {
            return new SkillCastResult(SkillCastStatus.NoEffect, skillId, skill, null, null, MulungEnergyReset: mulungEnergyReset);
        }

        if (effect.CooldownSeconds > 0 && player.SkillIsCooling(skillId, now))
        {
            return new SkillCastResult(SkillCastStatus.OnCooldown, skillId, skill, effect, null, MulungEnergyReset: mulungEnergyReset);
        }

        // P134：HP/MP 走 Java calcHPChange/calcMPChange（元素增幅、集中術、魔力無限、能量恢復），再登記 buff。
        var status = ApplySkillHpMp(player, effect).Status;
        var appliedBuff = status == SkillCastStatus.Success
            ? player.ApplyBuffStats(effect, now, GetMorphStatups(effect, player.Character.Gender))
            : null;

        // 對照 Java PlayerHandler.SpecialMove：有冷卻的技能施放時登記冷卻（呼叫端據此送 COOLDOWN
        // 封包）；海盜船例外——Java 施放時不登記，船被打爆時才由 MapleCharacter 登記冷卻。
        var cooldownStarted = 0;
        if (status == SkillCastStatus.Success && effect.CooldownSeconds > 0 && skillId != CorsairBattleshipSkillId)
        {
            player.AddSkillCooldown(skillId, now, effect.CooldownSeconds);
            cooldownStarted = effect.CooldownSeconds;
        }

        // P086：對照 Java applyTo 的 isTimeLeap() 分支——清除自己以外的所有冷卻（呼叫端逐一送 skillCooldown(id, 0)）。
        IReadOnlyList<int>? reset = status == SkillCastStatus.Success && skillId == TimeLeapSkillId
            ? player.ResetSkillCooldownsExcept(TimeLeapSkillId)
            : null;

        // P127：對照 Java applyTo：isDispel && makeChanceResult → dispelDebuffs；else isHeroWill → dispelDebuff(SEDUCE)。
        IReadOnlyList<PlayerDisease>? cured = null;
        if (status == SkillCastStatus.Success)
        {
            if (IsDispelSkill(skillId) && (effect.Prop == 100 || _random.Next(99) < effect.Prop))
            {
                cured = player.DispelDiseases();
            }
            else if (IsHeroWillSkill(skillId) && player.DispelDisease(PlayerDisease.Seduce))
            {
                cured = [PlayerDisease.Seduce];
            }
        }

        return new SkillCastResult(status, skillId, skill, effect, appliedBuff, cooldownStarted, reset, mulungEnergyReset,
            cured is { Count: > 0 } ? cured : null);
    }

    /// <summary>
    /// 攻擊技能（近戰/遠程/魔法）的冷卻檢查與登記。對照 Java <c>PlayerHandler.closeRangeAttack</c>/
    /// <c>rangedAttack</c>/<c>MagicDamage</c> 共用的冷卻區塊：技能等級以 <c>GameConstants.getLinkedSkill</c>
    /// 對應後的技能查詢，冷卻則以客戶端送來的原技能 ID 為鍵；冷卻中整次攻擊丟棄，否則登記冷卻。
    /// </summary>
    public AttackCooldownResult TryStartAttackCooldown(Player player, int skillId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (skillId == 0)
        {
            return new AttackCooldownResult(AttackCooldownStatus.NotApplicable, 0);
        }

        var linkedSkillId = GetLinkedSkillId(skillId);
        var skill = _skills.GetSkill(linkedSkillId);
        var level = player.GetSkillLevel(linkedSkillId);
        var effect = skill is null || level <= 0 ? null : skill.GetEffect(level);
        if (effect is null || effect.CooldownSeconds <= 0)
        {
            return new AttackCooldownResult(AttackCooldownStatus.NotApplicable, 0);
        }

        if (player.SkillIsCooling(skillId, now))
        {
            return new AttackCooldownResult(AttackCooldownStatus.OnCooldown, 0);
        }

        player.AddSkillCooldown(skillId, now, effect.CooldownSeconds);
        return new AttackCooldownResult(AttackCooldownStatus.Started, effect.CooldownSeconds);
    }

    /// <summary>
    /// P111：攻擊技能效果（對照 Java <c>AttackInfo.getAttackEffect</c>）：武陵/金字塔技能取第 1 級，
    /// 其他取 <c>getLinkedSkill</c> 對應技能的等級；技能 0、查無技能或等級 0 回 null。
    /// </summary>
    public MapleStatEffect? GetAttackEffect(Player player, int skillId)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (skillId == 0)
        {
            return null;
        }

        var linkedSkillId = GetLinkedSkillId(skillId);
        var skill = _skills.GetSkill(linkedSkillId);
        if (skill is null)
        {
            return null;
        }

        var level = IsMulungSkill(skillId) || IsPyramidSkill(skillId) ? 1 : player.GetSkillLevel(linkedSkillId);
        return level <= 0 ? null : skill.GetEffect(level);
    }

    /// <summary>
    /// P077：攻擊技能是否可用（對照 Java <c>AttackInfo.getAttackEffect</c>：武陵/金字塔技能強制視為等級 1；
    /// 其他技能以 <c>getLinkedSkill</c> 對應後的等級判斷，<c>skillLevel &lt;= 0</c> 回 null → 整次攻擊丟棄）。
    /// 技能 ID 0（普攻）一樣會得到 false，近戰/遠程呼叫端應先略過 0（Java 只在 <c>attack.skill != 0</c> 時檢查），
    /// 魔法攻擊則不略過（Java <c>MagicDamage</c> 無此判斷）。
    /// </summary>
    public static bool HasAttackSkillLevel(Player player, int skillId)
    {
        ArgumentNullException.ThrowIfNull(player);
        return IsMulungSkill(skillId) || IsPyramidSkill(skillId) || player.GetSkillLevel(GetLinkedSkillId(skillId)) > 0;
    }

    /// <summary>
    /// P108：武陵技能攻擊的能量判斷（呼叫端在套用傷害前呼叫）。近戰／遠程（Java <c>applyAttack</c>）需能量滿 10000；
    /// 魔法（<c>applyAttackMagic</c>）不檢查能量。兩者都必須在武陵道場（92502xxxx），通過時能量歸零。
    /// </summary>
    public static MulungAttackGate CheckMulungAttack(Player player, int skillId, bool requireFullEnergy)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!IsMulungSkill(skillId))
        {
            return MulungAttackGate.NotApplicable;
        }

        if (requireFullEnergy && player.MulungEnergy < Player.MaxMulungEnergy)
        {
            return MulungAttackGate.Blocked;
        }

        if (player.Character.MapId / 10000 != MulungDojoMapGroup)
        {
            return MulungAttackGate.Blocked;
        }

        player.ModifyMulungEnergy(increase: false);
        return MulungAttackGate.Consumed;
    }

    /// <summary>Java <c>MapleStatEffect.isDispel</c>：驅散 2311001 與 GM 技能 9101000/9001000。</summary>
    public static bool IsDispelSkill(int skillId) => skillId is 2311001 or 9101000 or 9001000;

    /// <summary>Java <c>MapleStatEffect.isHeroWill</c>（各職業英雄意志）。</summary>
    public static bool IsHeroWillSkill(int skillId) => skillId is 1121011 or 1221012 or 1321010 or 2121008 or 2221008
        or 2321009 or 3121009 or 3221008 or 4121009 or 4221008 or 5121008 or 5221010 or 21121008 or 22171004
        or 4341008 or 32121008 or 33121008 or 35121008;

    /// <summary>對照 Java <c>GameConstants.isMulungSkill</c>。</summary>
    public static bool IsMulungSkill(int skillId)
        => skillId is 1009 or 1010 or 1011
            or 10001009 or 10001010 or 10001011
            or 20001009 or 20001010 or 20001011
            or 20011009 or 20011010 or 20011011;

    /// <summary>對照 Java <c>GameConstants.isPyramidSkill</c>。</summary>
    public static bool IsPyramidSkill(int skillId)
        => skillId is 1020 or 10001020 or 20001020 or 20011020;

    /// <summary>對照 Java <c>GameConstants.getLinkedSkill</c>：衍生技能共用本體技能的等級。</summary>
    public static int GetLinkedSkillId(int skillId)
        => skillId switch
        {
            21110007 or 21110008 => 21110002,
            21120009 or 21120010 => 21120002,
            4321001 => 4321000,
            _ => skillId,
        };

    public CancelBuffResult CancelBuff(Player player, int sourceId)
    {
        ArgumentNullException.ThrowIfNull(player);

        var skill = _skills.GetSkill(sourceId);
        if (skill is null)
        {
            return new CancelBuffResult(CancelBuffStatus.UnknownSkill, sourceId, null, Array.Empty<PlayerBuffCancellation>());
        }

        if (skill.IsChargeSkill)
        {
            return new CancelBuffResult(CancelBuffStatus.ChargeSkill, sourceId, skill, Array.Empty<PlayerBuffCancellation>());
        }

        var canceled = player.CancelBuffBySource(sourceId);
        return canceled.Count == 0
            ? new CancelBuffResult(CancelBuffStatus.NoActiveBuff, sourceId, skill, canceled)
            : new CancelBuffResult(CancelBuffStatus.Success, sourceId, skill, canceled);
    }

    public IReadOnlyList<PlayerBuffCancellation> CancelExpiredBuffs(Player player, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.CancelExpiredBuffs(now);
    }

    /// <summary>P092：世界 tick 回復術週期回血（對照 Java <c>canRecover</c> + <c>doRecovery</c>）。</summary>
    public PeriodicBuffTick? TryRecover(Player player, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.TryRecover(now);
    }

    /// <summary>P093：世界 tick 龍之魂週期扣血，只對職業 131/132（對照 Java <c>handleCooldowns</c> 的 job 判斷 + <c>doDragonBlood</c>）。</summary>
    public PeriodicBuffTick? TryDragonBlood(Player player, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.Character.Job is 131 or 132 ? player.TryDragonBlood(now) : null;
    }

    /// <summary>
    /// P094：狂戰士狀態檢查（對照 Java <c>canBerserk</c> + <c>doBerserk</c>）：只對職業 132、每 10 秒一次；
    /// 1320006 等級 ≥ 1 時回傳 <c>hp &lt;= maxHp * x / 100</c> 的結果（呼叫端送特效），否則回 null（Java 設 -1，下次繼續檢查）。
    /// </summary>
    public bool? TryCheckBerserk(Player player, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.Character.Job != 132 || !player.CanCheckBerserk(now))
        {
            return null;
        }

        var level = player.GetSkillLevel(BerserkSkillId);
        var effect = level >= 1 ? _skills.GetSkill(BerserkSkillId)?.GetEffect(level) : null;
        if (effect is null)
        {
            return null;
        }

        player.MarkBerserkChecked(now);
        return player.Hp <= player.MaxHp * (effect.X / 100.0);
    }

    /// <summary>世界 tick 冷卻到期（對照 Java <c>World.handleCooldowns</c>）：回傳本次移除的技能 ID。</summary>
    public IReadOnlyList<int> ExpireSkillCooldowns(Player player, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.RemoveExpiredSkillCooldowns(now);
    }

    public AranComboResult AddAranCombo(Player player, int amount, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.Character.Job is < 2000 or > 2112)
        {
            return new AranComboResult(AranComboStatus.NotAranJob, player.AranComboCount, 0, null, null, null);
        }

        var combo = player.AddAranCombo(amount, now);
        var requiredLevel = combo is >= 10 and <= 100 && combo % 10 == 0
            ? combo / 10
            : 0;

        if (requiredLevel == 0)
        {
            return new AranComboResult(AranComboStatus.Success, combo, requiredLevel, null, null, null);
        }

        if (player.GetSkillLevel(21000000) < requiredLevel)
        {
            return new AranComboResult(AranComboStatus.SkillLevelTooLow, combo, requiredLevel, null, null, null);
        }

        var skill = _skills.GetSkill(21000000);
        var effect = skill?.GetEffect(requiredLevel) ?? new MapleStatEffect
        {
            SourceId = 21000000,
            Level = (byte)requiredLevel,
            IsOverTime = true,
            DurationMilliseconds = 99_999,
            IsCombo = true,
        };

        // Java MapleStatEffect.applyComboBuff uses a hard-coded 99999ms duration.
        // TODO(P003-D4 data): validate 21000000 level effect timing against Skill.wz/live client.
        var applied = player.ApplyAranComboBuff(21000000, (byte)requiredLevel, combo, 99_999, now);
        return new AranComboResult(AranComboStatus.Success, combo, requiredLevel, skill, effect, applied);
    }
}
