using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Skills;

/// <summary>P156：技能變身的 Selfstat（Java applyBuffEffect isMorph()/isPirateMorph()、getMorph(chr)）。</summary>
public sealed class SkillMorphTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1, 1100)] // 女性 +100
    public void PirateTransformation_RegistersSelfstatWithGenderMorph(byte gender, int expectedMorph)
    {
        var skill = new MapleSkill
        {
            Id = 5111005,
            Effects =
            [
                new MapleStatEffect
                {
                    SourceId = 5111005,
                    Level = 1,
                    IsOverTime = true,
                    DurationMilliseconds = 60_000,
                    Jump = 10,
                    Wdef = 30,
                    Speed = 20,
                    Watk = 99, // Java Selfstat 不含 WATK（照抄）
                    Statups = [new BuffStatValue(MapleBuffStat.WATK, 99)],
                },
            ],
        };
        var player = new Player(new Character { Id = 1, Name = "Pirate", Gender = gender }, new Position(0, 0, 0, 0));
        player.ChangeSkillLevel(5111005, 1, 1);

        var result = new SkillService(new InMemorySkillCatalog([skill])).Cast(player, 5111005, 1, T0);

        Assert.Equal(
            [
                new BuffStatValue(MapleBuffStat.JUMP, 10),
                new BuffStatValue(MapleBuffStat.WDEF, 30),
                new BuffStatValue(MapleBuffStat.MDEF, 30),
                new BuffStatValue(MapleBuffStat.SPEED, 20),
                new BuffStatValue(MapleBuffStat.MORPH, expectedMorph),
            ],
            result.AppliedBuff!.Stats);
        Assert.Equal(expectedMorph, player.GetBuffValue(MapleBuffStat.MORPH));
        Assert.Null(player.GetBuffValue(MapleBuffStat.WATK));
    }

    [Theory]
    [InlineData(5121003, 0, 1001)]
    [InlineData(5101007, 1, 1002)] // 1002 不加性別
    [InlineData(2001002, 0, 0)]
    public void GetMorphValue_MatchesJava(int skillId, int gender, int expected)
        => Assert.Equal(expected, SkillService.GetMorphValue(new MapleStatEffect { SourceId = skillId }, gender));

    [Fact]
    public void WzMorph_IsMorphBuff_NonMorphIsNot()
    {
        Assert.True(SkillService.IsMorphBuff(new MapleStatEffect { SourceId = 4001003, MorphId = 3 }));
        Assert.Null(SkillService.GetMorphStatups(new MapleStatEffect { SourceId = 2001002 }, 0));
    }
}
