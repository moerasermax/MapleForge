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
    IReadOnlyList<int>? ResetCooldownSkillIds = null);

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

public sealed class SkillService
{
    /// <summary>槍神「海盜船」（Java <c>槍神.海盜船</c>）：施放時不登記冷卻。</summary>
    public const int CorsairBattleshipSkillId = 5221006;

    /// <summary>拳霸「時間置換」（Java <c>isTimeLeap()</c>：<c>sourceid == 5121010</c>）。</summary>
    public const int TimeLeapSkillId = 5121010;

    private readonly ISkillCatalog _skills;

    public SkillService(ISkillCatalog skills)
    {
        _skills = skills;
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

        var serverLevel = player.GetSkillLevel(skillId);
        if (serverLevel <= 0)
        {
            return new SkillCastResult(SkillCastStatus.SkillNotLearned, skillId, skill, null, null);
        }

        if (serverLevel != clientSkillLevel)
        {
            return new SkillCastResult(SkillCastStatus.LevelMismatch, skillId, skill, null, null);
        }

        var effect = skill.GetEffect(serverLevel);
        if (effect is null)
        {
            return new SkillCastResult(SkillCastStatus.NoEffect, skillId, skill, null, null);
        }

        if (effect.CooldownSeconds > 0 && player.SkillIsCooling(skillId, now))
        {
            return new SkillCastResult(SkillCastStatus.OnCooldown, skillId, skill, effect, null);
        }

        var applied = player.ApplySkillEffect(effect, now);
        var status = applied.Status switch
        {
            PlayerSkillApplicationStatus.Applied => SkillCastStatus.Success,
            PlayerSkillApplicationStatus.NotEnoughHp => SkillCastStatus.NotEnoughHp,
            PlayerSkillApplicationStatus.NotEnoughMp => SkillCastStatus.NotEnoughMp,
            _ => SkillCastStatus.NoEffect,
        };

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

        return new SkillCastResult(status, skillId, skill, effect, applied.Buff, cooldownStarted, reset);
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
