using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Skills;

/// <summary>P081：<see cref="SummonService.TrySpawn"/> 對照 Java <c>MapleStatEffect.applyTo</c> 的召喚獸建立。</summary>
public sealed class SummonServiceTests
{
    private static readonly Position Pos = new(100, 200, 0, 5);

    [Fact]
    public void TrySpawn_SummonSkill_AddsSummonWithJavaHpAndMovementType()
    {
        var field = new FieldInstance(100000000);
        var owner = NewPlayer(7);

        var result = new SummonService().TrySpawn(field, owner, 3111005, 10, new MapleStatEffect { SourceId = 3111005, X = 30 }, Pos);

        Assert.NotNull(result);
        var summon = result!.Summon;
        Assert.Same(summon, field.Get(summon.ObjectId));
        Assert.Equal((7, 3111005, (byte)10, (short)30), (summon.OwnerId, summon.SkillId, summon.SkillLevel, summon.Hp));
        Assert.Equal(SummonMovementType.CircleFollow, summon.MovementType);
        Assert.Equal(Pos, summon.Position);
        Assert.True(summon.ObjectId >= SummonService.SummonObjectIdBase);
        Assert.Null(result.Replaced);
    }

    [Fact]
    public void TrySpawn_Beholder_GetsExtraHp()
    {
        var result = new SummonService().TrySpawn(
            new FieldInstance(100000000), NewPlayer(7), SummonService.BeholderSkillId, 1, new MapleStatEffect { X = 0 }, Pos);

        Assert.Equal(1, result!.Summon.Hp);
    }

    [Fact]
    public void TrySpawn_NonSummonSkill_ReturnsNull()
    {
        var field = new FieldInstance(100000000);

        Assert.Null(new SummonService().TrySpawn(field, NewPlayer(7), 2001002, 1, new MapleStatEffect(), Pos));
        Assert.Empty(field.Objects);
    }

    [Fact]
    public void TrySpawn_RecastSameSkill_ReplacesOldSummonOnlyForSameOwner()
    {
        var field = new FieldInstance(100000000);
        var service = new SummonService();
        var effect = new MapleStatEffect { X = 10 };
        var mine = service.TrySpawn(field, NewPlayer(7), 5211001, 1, effect, Pos)!.Summon;
        var others = service.TrySpawn(field, NewPlayer(8), 5211001, 1, effect, Pos)!.Summon;

        var recast = service.TrySpawn(field, NewPlayer(7), 5211001, 1, effect, Pos)!;

        Assert.Same(mine, recast.Replaced);
        Assert.Null(field.Get(mine.ObjectId));
        Assert.Same(others, field.Get(others.ObjectId));
        Assert.Same(recast.Summon, field.Get(recast.Summon.ObjectId));
        Assert.NotEqual(mine.ObjectId, recast.Summon.ObjectId);
    }

    [Theory]
    [InlineData(5211001, SummonMovementType.Stationary)]
    [InlineData(2311006, SummonMovementType.CircleFollow)]
    [InlineData(5211002, SummonMovementType.CircleStationary)]
    [InlineData(32111006, SummonMovementType.WalkStationary)]
    [InlineData(2121005, SummonMovementType.Follow)]
    public void GetMovementType_MatchesJavaTable(int skillId, SummonMovementType expected)
    {
        Assert.Equal(expected, Summon.GetMovementType(skillId));
    }

    private static Player NewPlayer(int id)
        => new(new Character { Id = id, Name = $"P{id}" }, new Position(0, 0, 0, 0));
}
