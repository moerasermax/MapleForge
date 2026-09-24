using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Skills;

public sealed class SkillServiceTests
{
    [Fact]
    public void Cast_ValidatesLevel_ConsumesMp_AndAppliesBuff()
    {
        var player = MakePlayer(mp: 50);
        player.ChangeSkillLevel(2001002, level: 3, masterLevel: 20);
        var service = new SkillService(new InMemorySkillCatalog(new[] { MagicGuardSkill() }));
        var now = new DateTimeOffset(2026, 6, 6, 1, 2, 3, TimeSpan.Zero);

        var result = service.Cast(player, 2001002, clientSkillLevel: 3, now);

        Assert.Equal(SkillCastStatus.Success, result.Status);
        Assert.NotNull(result.AppliedBuff);
        Assert.Equal(39, player.Mp);
        Assert.Single(player.ActiveBuffs);
    }

    [Fact]
    public void Cast_RejectsClientLevelMismatch()
    {
        var player = MakePlayer(mp: 50);
        player.ChangeSkillLevel(2001002, level: 3, masterLevel: 20);
        var service = new SkillService(new InMemorySkillCatalog(new[] { MagicGuardSkill() }));

        var result = service.Cast(player, 2001002, clientSkillLevel: 2, DateTimeOffset.UtcNow);

        Assert.Equal(SkillCastStatus.LevelMismatch, result.Status);
        Assert.Empty(player.ActiveBuffs);
    }

    [Fact]
    public void CancelBuff_RemovesActiveBuffBySource()
    {
        var player = MakePlayer(mp: 50);
        player.ChangeSkillLevel(2001002, level: 3, masterLevel: 20);
        var service = new SkillService(new InMemorySkillCatalog(new[] { MagicGuardSkill() }));
        service.Cast(player, 2001002, clientSkillLevel: 3, DateTimeOffset.UtcNow);

        var result = service.CancelBuff(player, 2001002);

        Assert.Equal(CancelBuffStatus.Success, result.Status);
        Assert.Equal(new[] { MapleBuffStat.MAGIC_GUARD }, Assert.Single(result.Cancellations).Stats);
        Assert.Empty(player.ActiveBuffs);
    }

    [Fact]
    public void AddAranCombo_AppliesComboBuffAtJavaThreshold()
    {
        var player = MakePlayer(mp: 50, job: 2000);
        player.ChangeSkillLevel(21000000, level: 1, masterLevel: 10);
        player.AddAranCombo(9, DateTimeOffset.UtcNow);
        var service = new SkillService(new InMemorySkillCatalog(new[] { AranComboSkill() }));
        var now = new DateTimeOffset(2026, 7, 6, 1, 2, 3, TimeSpan.Zero);

        var result = service.AddAranCombo(player, amount: 1, now);

        Assert.Equal(AranComboStatus.Success, result.Status);
        Assert.Equal(10, result.Combo);
        Assert.Equal(1, result.RequiredSkillLevel);
        Assert.NotNull(result.AppliedBuff);
        Assert.Equal(new[] { new BuffStatValue(MapleBuffStat.ARAN_COMBO, 10) }, result.AppliedBuff!.Stats);
    }

    [Fact]
    public void AddAranCombo_RejectsNonAranJob()
    {
        var player = MakePlayer(mp: 50, job: 100);
        var service = new SkillService(new InMemorySkillCatalog(new[] { AranComboSkill() }));

        var result = service.AddAranCombo(player, amount: 1, DateTimeOffset.UtcNow);

        Assert.Equal(AranComboStatus.NotAranJob, result.Status);
        Assert.Equal(0, player.AranComboCount);
        Assert.Empty(player.ActiveBuffs);
    }

    [Fact]
    public void TryStartAttackCooldown_StartsThenBlocksUntilExpired()
    {
        var player = MakePlayer(mp: 50);
        player.ChangeSkillLevel(1121008, level: 1, masterLevel: 10);
        var service = new SkillService(new InMemorySkillCatalog(new[] { AttackCooldownSkill(1121008, seconds: 10) }));
        var now = new DateTimeOffset(2026, 9, 24, 1, 0, 0, TimeSpan.Zero);

        var first = service.TryStartAttackCooldown(player, 1121008, now);
        var cooling = service.TryStartAttackCooldown(player, 1121008, now.AddSeconds(9));
        var afterExpiry = service.TryStartAttackCooldown(player, 1121008, now.AddSeconds(11));

        Assert.Equal(new AttackCooldownResult(AttackCooldownStatus.Started, 10), first);
        Assert.Equal(AttackCooldownStatus.OnCooldown, cooling.Status);
        Assert.Equal(AttackCooldownStatus.Started, afterExpiry.Status);
    }

    [Theory]
    [InlineData(0)]          // 普攻
    [InlineData(1001004)]    // 目錄沒有的技能
    public void TryStartAttackCooldown_NormalAttackOrUnknownSkill_NotApplicable(int skillId)
    {
        var player = MakePlayer(mp: 50);
        var service = new SkillService(new InMemorySkillCatalog(new[] { AttackCooldownSkill(1121008, seconds: 10) }));

        var result = service.TryStartAttackCooldown(player, skillId, DateTimeOffset.UnixEpoch);

        Assert.Equal(AttackCooldownStatus.NotApplicable, result.Status);
    }

    [Fact]
    public void TryStartAttackCooldown_UnlearnedSkill_NotApplicable()
    {
        var player = MakePlayer(mp: 50);
        var service = new SkillService(new InMemorySkillCatalog(new[] { AttackCooldownSkill(1121008, seconds: 10) }));

        var result = service.TryStartAttackCooldown(player, 1121008, DateTimeOffset.UnixEpoch);

        Assert.Equal(AttackCooldownStatus.NotApplicable, result.Status);
        Assert.False(player.SkillIsCooling(1121008, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void TryStartAttackCooldown_LinkedSkill_UsesBaseSkillLevelButKeysCooldownOnOriginalId()
    {
        // 對照 Java GameConstants.getLinkedSkill：21110007 的等級取自 21110002，冷卻以 21110007 為鍵。
        var player = MakePlayer(mp: 50);
        player.ChangeSkillLevel(21110002, level: 1, masterLevel: 20);
        var service = new SkillService(new InMemorySkillCatalog(new[] { AttackCooldownSkill(21110002, seconds: 5) }));
        var now = DateTimeOffset.UnixEpoch;

        var result = service.TryStartAttackCooldown(player, 21110007, now);

        Assert.Equal(new AttackCooldownResult(AttackCooldownStatus.Started, 5), result);
        Assert.True(player.SkillIsCooling(21110007, now.AddSeconds(1)));
        Assert.False(player.SkillIsCooling(21110002, now.AddSeconds(1)));
    }

    [Fact]
    public void HasAttackSkillLevel_RequiresLearnedSkill_ExceptMulungAndPyramid()
    {
        var player = MakePlayer(mp: 50);
        player.ChangeSkillLevel(1001004, level: 1, masterLevel: 20);

        Assert.True(SkillService.HasAttackSkillLevel(player, 1001004));
        Assert.False(SkillService.HasAttackSkillLevel(player, 1001005)); // 未學
        Assert.False(SkillService.HasAttackSkillLevel(player, 0));       // 技能 0 等級恆為 0（魔法攻擊 Java 也會丟棄）
        Assert.True(SkillService.HasAttackSkillLevel(player, 1009));     // 武陵技能：Java 強制等級 1
        Assert.True(SkillService.HasAttackSkillLevel(player, 20001020)); // 金字塔技能
    }

    [Fact]
    public void HasAttackSkillLevel_LinkedSkill_UsesBaseSkillLevel()
    {
        var player = MakePlayer(mp: 50);
        player.ChangeSkillLevel(21120002, level: 1, masterLevel: 30);

        Assert.True(SkillService.HasAttackSkillLevel(player, 21120009));
        Assert.True(SkillService.HasAttackSkillLevel(player, 21120010));
        Assert.False(SkillService.HasAttackSkillLevel(player, 21110007)); // 本體 21110002 未學
    }

    [Theory]
    [InlineData((short)131, true)]
    [InlineData((short)132, true)]
    [InlineData((short)130, false)]
    [InlineData((short)111, false)]
    public void TryDragonBlood_OnlyDragonKnightJobs(short job, bool drains)
    {
        // P093：Java handleCooldowns 只對 job 131/132 呼叫 doDragonBlood。
        var player = MakePlayer(mp: 50, job: job);
        player.Character.Stats.Hp = 100;
        player.Character.Stats.MaxHp = 100;
        var start = DateTimeOffset.UnixEpoch;
        player.ApplySkillEffect(new MapleStatEffect
        {
            SourceId = 1311008,
            Level = 1,
            IsOverTime = true,
            DurationMilliseconds = 60_000,
            Statups = new[] { new BuffStatValue(MapleBuffStat.DRAGONBLOOD, 20) },
        }, start);
        var service = new SkillService(new InMemorySkillCatalog(Array.Empty<MapleSkill>()));

        var tick = service.TryDragonBlood(player, start.AddSeconds(5));

        Assert.Equal(drains, tick is not null);
    }

    private static MapleSkill AttackCooldownSkill(int skillId, int seconds)
        => new()
        {
            Id = skillId,
            Effects = new[]
            {
                new MapleStatEffect { SourceId = skillId, Level = 1, CooldownSeconds = seconds },
            },
        };

    private static MapleSkill MagicGuardSkill()
        => new()
        {
            Id = 2001002,
            Name = "Magic Guard",
            MasterLevel = 20,
            Effects = new[]
            {
                new MapleStatEffect
                {
                    SourceId = 2001002,
                    Level = 1,
                    IsOverTime = true,
                    DurationMilliseconds = 100_000,
                    MpCon = 9,
                    Statups = new[] { new BuffStatValue(MapleBuffStat.MAGIC_GUARD, 10) },
                },
                new MapleStatEffect
                {
                    SourceId = 2001002,
                    Level = 2,
                    IsOverTime = true,
                    DurationMilliseconds = 150_000,
                    MpCon = 10,
                    Statups = new[] { new BuffStatValue(MapleBuffStat.MAGIC_GUARD, 20) },
                },
                new MapleStatEffect
                {
                    SourceId = 2001002,
                    Level = 3,
                    IsOverTime = true,
                    DurationMilliseconds = 200_000,
                    MpCon = 11,
                    Statups = new[] { new BuffStatValue(MapleBuffStat.MAGIC_GUARD, 30) },
                },
            },
        };

    private static MapleSkill AranComboSkill()
        => new()
        {
            Id = 21000000,
            Name = "Combo Ability",
            MasterLevel = 10,
            Effects = Enumerable.Range(1, 10)
                .Select(level => new MapleStatEffect
                {
                    SourceId = 21000000,
                    Level = (byte)level,
                    IsOverTime = true,
                    DurationMilliseconds = 99_999,
                    IsCombo = true,
                })
                .ToArray(),
        };

    private static Player MakePlayer(short mp, short job = 0)
    {
        var chr = new Character
        {
            Id = 1,
            Name = "Skill",
            Job = job,
            Stats = new CharacterStats { Hp = 50, MaxHp = 50, Mp = mp, MaxMp = 50 },
        };
        return new Player(chr, new Position(0, 0, 0, 0));
    }
}
