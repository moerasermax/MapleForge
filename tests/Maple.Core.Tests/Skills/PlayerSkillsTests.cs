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
