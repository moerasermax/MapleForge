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
