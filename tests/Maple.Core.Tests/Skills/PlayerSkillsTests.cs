using Maple.Core.Characters;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Core.Tests.Skills;

public sealed class PlayerSkillsTests
{
    [Fact]
    public void ChangeSkillLevel_UpsertsPersistentSkillRecord()
    {
        var player = MakePlayer();

        player.ChangeSkillLevel(2001002, level: 3, masterLevel: 20);
        player.ChangeSkillLevel(2001002, level: 4, masterLevel: 20);

        var skill = Assert.Single(player.Character.Skills);
        Assert.Equal(2001002, skill.SkillId);
        Assert.Equal(4, player.GetSkillLevel(2001002));
        Assert.Equal(20, player.GetMasterLevel(2001002));
    }

    [Fact]
    public void ApplySkillEffect_ConsumesMpAndRegistersBuffStats()
    {
        var player = MakePlayer(mp: 50);
        var now = new DateTimeOffset(2026, 6, 6, 1, 2, 3, TimeSpan.Zero);
        var effect = new MapleStatEffect
        {
            SourceId = 2001002,
            Level = 3,
            IsOverTime = true,
            DurationMilliseconds = 200_000,
            MpCon = 11,
            Statups = new[] { new BuffStatValue(MapleBuffStat.MAGIC_GUARD, 30) },
        };

        var applied = player.ApplySkillEffect(effect, now);

        Assert.Equal(PlayerSkillApplicationStatus.Applied, applied.Status);
        Assert.Equal(39, player.Mp);
        var buff = Assert.Single(player.ActiveBuffs);
        Assert.Equal(MapleBuffStat.MAGIC_GUARD, buff.Stat);
        Assert.Equal(30, buff.Value);
        Assert.Equal(2001002, buff.SourceId);
        Assert.Equal(now.AddMilliseconds(200_000), buff.ExpiresAt);
    }

    [Fact]
    public void CancelExpiredBuffs_RemovesAndReturnsStats()
    {
        var player = MakePlayer(mp: 50);
        var now = new DateTimeOffset(2026, 6, 6, 1, 2, 3, TimeSpan.Zero);
        var effect = new MapleStatEffect
        {
            SourceId = 2001002,
            Level = 1,
            IsOverTime = true,
            DurationMilliseconds = 1_000,
            Statups = new[] { new BuffStatValue(MapleBuffStat.MAGIC_GUARD, 30) },
        };
        player.ApplySkillEffect(effect, now);

        var canceled = player.CancelExpiredBuffs(now.AddMilliseconds(1_001));

        var cancellation = Assert.Single(canceled);
        Assert.Equal(2001002, cancellation.SourceId);
        Assert.Equal(new[] { MapleBuffStat.MAGIC_GUARD }, cancellation.Stats);
        Assert.Empty(player.ActiveBuffs);
    }

    [Fact]
    public void AddAranCombo_ResetsAfterJavaTimeoutAndCapsAtMaximum()
    {
        var player = MakePlayer();
        var now = new DateTimeOffset(2026, 7, 6, 1, 2, 3, TimeSpan.Zero);

        Assert.Equal(9, player.AddAranCombo(9, now));
        Assert.Equal(1, player.AddAranCombo(1, now.AddMilliseconds(4_001)));
        Assert.Equal(30_000, player.AddAranCombo(40_000, now.AddMilliseconds(4_002)));
    }

    [Fact]
    public void ApplyAranComboBuff_RegistersRuntimeAranComboStat()
    {
        var player = MakePlayer();
        var now = new DateTimeOffset(2026, 7, 6, 1, 2, 3, TimeSpan.Zero);

        var applied = player.ApplyAranComboBuff(21000000, skillLevel: 1, combo: 10, durationMilliseconds: 99_999, now);

        Assert.Equal(21000000, applied.SourceId);
        Assert.Equal(new[] { new BuffStatValue(MapleBuffStat.ARAN_COMBO, 10) }, applied.Stats);
        var buff = Assert.Single(player.ActiveBuffs);
        Assert.Equal(MapleBuffStat.ARAN_COMBO, buff.Stat);
        Assert.Equal(10, buff.Value);
        Assert.Equal(now.AddMilliseconds(99_999), buff.ExpiresAt);
    }

    private static Player MakePlayer(short mp = 5)
    {
        var chr = new Character
        {
            Id = 1,
            Name = "Skill",
            Stats = new CharacterStats { Hp = 50, MaxHp = 50, Mp = mp, MaxMp = 50 },
        };
        return new Player(chr, new Position(0, 0, 0, 0));
    }
}
