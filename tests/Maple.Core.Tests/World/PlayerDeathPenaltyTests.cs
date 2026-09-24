using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

/// <summary>P076：<see cref="Player.ApplyDeathPenalty"/> 對照 Java <c>MapleCharacter.playerDead</c> 經驗值區塊。</summary>
public sealed class PlayerDeathPenaltyTests
{
    [Theory]
    [InlineData((short)0)]
    [InlineData((short)1000)]
    [InlineData((short)2000)]
    [InlineData((short)2001)]
    [InlineData((short)3000)]
    public void BeginnerJobs_AreExempt(short job)
    {
        var player = NewPlayer(job, level: 10, exp: 1000);

        var result = player.ApplyDeathPenalty(reducedExpLoss: false);

        Assert.Equal(DeathPenaltyKind.ExemptJob, result.Kind);
        Assert.Equal(1000, player.Character.Exp);
    }

    [Fact]
    public void Warrior_FieldDeath_LosesJavaFormulaExp()
    {
        // 等級 10 升級需 1716；(0.2f / 4 + 0.05) → 0.1f；1716 * 0.1f = 171.6 → 171。
        var player = NewPlayer(job: 100, level: 10, exp: 1000);

        var result = player.ApplyDeathPenalty(reducedExpLoss: false);

        Assert.Equal(DeathPenaltyKind.ExpLost, result.Kind);
        Assert.Equal(171, result.ExpLost);
        Assert.Equal(829, player.Character.Exp);
    }

    [Fact]
    public void Archer_UsesSmallerJobFactor()
    {
        // 職業系 3：(0.08f / 4 + 0.05) → 0.07f；1716 * 0.07f ≈ 120.12 → 120。
        var player = NewPlayer(job: 300, level: 10, exp: 1000);

        player.ApplyDeathPenalty(reducedExpLoss: false);

        Assert.Equal(880, player.Character.Exp);
    }

    [Fact]
    public void TownOrRegularExpLossMap_LosesOnePercent()
    {
        var player = NewPlayer(job: 100, level: 10, exp: 1000);

        player.ApplyDeathPenalty(reducedExpLoss: true);

        Assert.Equal(1000 - 17, player.Character.Exp); // 1716 * 0.01f = 17.16 → 17
    }

    [Fact]
    public void ExpNeverGoesBelowZero()
    {
        var player = NewPlayer(job: 100, level: 10, exp: 5);

        var result = player.ApplyDeathPenalty(reducedExpLoss: false);

        Assert.Equal(0, player.Character.Exp);
        Assert.Equal(5, result.ExpLost);
    }

    [Fact]
    public void SafetyCharm_ConsumedInsteadOfExpLoss_ThenSuperCharm()
    {
        var player = NewPlayer(job: 100, level: 10, exp: 1000);
        player.GainItem(InventoryType.Cash, Player.SafetyCharmItemId, 2);
        player.GainItem(InventoryType.Cash, Player.SuperSafetyCharmItemId, 1);

        var first = player.ApplyDeathPenalty(reducedExpLoss: false);
        var second = player.ApplyDeathPenalty(reducedExpLoss: false);
        var third = player.ApplyDeathPenalty(reducedExpLoss: false);
        var fourth = player.ApplyDeathPenalty(reducedExpLoss: false);

        Assert.Equal((DeathPenaltyKind.CharmConsumed, 1), (first.Kind, first.CharmsLeft));
        Assert.Equal(Player.SafetyCharmItemId, Assert.Single(first.CharmMutations).ItemId);
        Assert.Equal((DeathPenaltyKind.CharmConsumed, 0), (second.Kind, second.CharmsLeft));
        Assert.Equal(DeathPenaltyKind.CharmConsumed, third.Kind);
        Assert.Equal(Player.SuperSafetyCharmItemId, Assert.Single(third.CharmMutations).ItemId);
        Assert.Equal(1000, 1000 + first.ExpLost + second.ExpLost + third.ExpLost);
        Assert.Equal(DeathPenaltyKind.ExpLost, fourth.Kind);
        Assert.Equal(829, player.Character.Exp);
    }

    private static Player NewPlayer(short job, byte level, int exp)
        => new(new Character
        {
            Id = 1,
            Name = "Dead",
            Job = job,
            Level = level,
            Exp = exp,
            Stats = new CharacterStats { Hp = 0, MaxHp = 100, Luk = 4 },
        }, new Position(0, 0, 0, 0));
}
