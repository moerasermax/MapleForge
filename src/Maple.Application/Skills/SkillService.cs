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
}
