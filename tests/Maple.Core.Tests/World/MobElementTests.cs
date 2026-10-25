using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

/// <summary>P114：對照 Java <c>decodeElementalString</c> 與 <c>applyStatus</c> 的屬性抗性判斷。</summary>
public sealed class MobElementTests
{
    [Fact]
    public void DecodeResistances_ParsesPairs()
    {
        var map = ElementCodec.DecodeResistances("F2I3S1");

        Assert.Equal(ElementalEffectiveness.Strong, map[Element.Fire]);
        Assert.Equal(ElementalEffectiveness.Weak, map[Element.Ice]);
        Assert.Equal(ElementalEffectiveness.Immune, map[Element.Poison]);
    }

    [Fact]
    public void DecodeResistances_SkipsUnknownPairs()
    {
        var map = ElementCodec.DecodeResistances("X2H3");

        Assert.Equal(ElementalEffectiveness.Weak, Assert.Single(map).Value);
    }

    [Theory]
    [InlineData("F2", Element.Fire, MonsterStatusApplyResult.Immune)]
    [InlineData("F1", Element.Fire, MonsterStatusApplyResult.Immune)]
    [InlineData("F3", Element.Fire, MonsterStatusApplyResult.Applied)]
    [InlineData("I2", Element.Fire, MonsterStatusApplyResult.Applied)]
    public void TryApplyStatus_SourceElementResisted_Blocks(string elemAttr, Element element, MonsterStatusApplyResult expected)
    {
        var mob = MakeMob(elemAttr);

        Assert.Equal(expected, mob.TryApplyStatus(Effect(MonsterStatus.Speed, 2101003), sourceElement: element));
    }

    [Fact]
    public void TryApplyStatus_FirePoisonCompo_ChecksPoisonResistance()
    {
        var mob = MakeMob("S2");

        Assert.Equal(
            MonsterStatusApplyResult.Immune,
            mob.TryApplyStatus(Effect(MonsterStatus.Poison, 2111006), sourceElement: Element.Neutral));
    }

    [Fact]
    public void TryApplyStatus_VenomOnPoisonWeakMob_BlockedAsInJava()
    {
        var mob = MakeMob("S3");

        Assert.Equal(
            MonsterStatusApplyResult.Immune,
            mob.TryApplyStatus(Effect(MonsterStatus.Poison, 4120005), sourceElement: Element.Neutral));
    }

    [Fact]
    public void TryApplyStatus_NoSourceElement_SkipsElementCheck()
    {
        var mob = MakeMob("F1");

        Assert.Equal(MonsterStatusApplyResult.Applied, mob.TryApplyStatus(Effect(MonsterStatus.Speed, 0)));
    }

    private static MonsterStatusEffect Effect(MonsterStatus status, int skillId)
        => new(status, 1, skillId, null, null);

    private static Mob MakeMob(string elemAttr)
        => new(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, 100, MaxMp: 0, Level: 1, Exp: 1, Resistances: ElementCodec.DecodeResistances(elemAttr)),
            objectId: 100001);
}
