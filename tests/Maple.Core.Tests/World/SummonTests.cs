using Maple.Core.World;

namespace Maple.Core.Tests.World;

public sealed class SummonTests
{
    [Fact]
    public void Summon_IsFieldObject_WithSummonType()
    {
        var summon = CreateSummon();

        Assert.Equal(200001, summon.ObjectId);
        Assert.Equal(FieldObjectType.Summon, summon.Type);
        Assert.Equal(new Position(10, 20, 4, 7), summon.Position);
    }

    [Fact]
    public void TakeDamage_ReducesHp()
    {
        var summon = CreateSummon(hp: 100);

        var applied = summon.TakeDamage(35);

        Assert.Equal(35, applied);
        Assert.Equal((short)65, summon.Hp);
    }

    [Fact]
    public void TakeDamage_ClampsAtZero()
    {
        var summon = CreateSummon(hp: 30);

        var applied = summon.TakeDamage(100);

        Assert.Equal(30, applied);
        Assert.Equal((short)0, summon.Hp);
    }

    [Theory]
    [InlineData(3111002)]
    [InlineData(3211002)]
    [InlineData(13111004)]
    [InlineData(4341006)]
    [InlineData(33111003)]
    public void IsPuppet_ReturnsTrue_ForJavaPuppetSkills(int skillId)
    {
        var summon = CreateSummon(skillId: skillId);

        Assert.True(summon.IsPuppet);
    }

    [Fact]
    public void IsPuppet_ReturnsFalse_ForRegularSummon()
    {
        var summon = CreateSummon(skillId: 1321007);

        Assert.False(summon.IsPuppet);
    }

    [Fact]
    public void MoveTo_UpdatesPosition()
    {
        var summon = CreateSummon();

        summon.MoveTo(new Position(30, 40, 5, 8));

        Assert.Equal(new Position(30, 40, 5, 8), summon.Position);
    }

    private static Summon CreateSummon(int skillId = 1321007, short hp = 100)
        => new(
            objectId: 200001,
            skillId: skillId,
            skillLevel: 10,
            ownerId: 1,
            hp: hp,
            movementType: SummonMovementType.Follow,
            position: new Position(10, 20, 4, 7));
}
