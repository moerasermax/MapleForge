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
    PlayerBuffChange? AppliedBuff);

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

        if (status == SkillCastStatus.Success && effect.CooldownSeconds > 0)
        {
            player.AddSkillCooldown(skillId, now, effect.CooldownSeconds);
        }

        return new SkillCastResult(status, skillId, skill, effect, applied.Buff);
    }

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
