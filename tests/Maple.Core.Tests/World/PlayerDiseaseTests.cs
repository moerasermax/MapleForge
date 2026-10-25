using Maple.Core.Characters;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

/// <summary>P126：對照 Java MapleCharacter.getDiseaseBuff / dispelDebuff / handleCooldowns 異常到期。</summary>
public sealed class PlayerDiseaseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryGiveDisease_AtMostTwoAndNoDuplicates()
    {
        var player = NewPlayer();

        Assert.NotNull(player.TryGiveDisease(PlayerDisease.Seal, 1, 5000, 120, 1, T0));
        Assert.Null(player.TryGiveDisease(PlayerDisease.Seal, 1, 5000, 120, 1, T0));
        Assert.NotNull(player.TryGiveDisease(PlayerDisease.Darkness, 1, 5000, 121, 1, T0));
        Assert.Null(player.TryGiveDisease(PlayerDisease.Weaken, 1, 5000, 122, 1, T0));
        Assert.Equal(2, player.Diseases.Count);
    }

    [Theory]
    [InlineData(PlayerDisease.Seal, false)]
    [InlineData(PlayerDisease.Stun, true)]
    [InlineData(PlayerDisease.Seduce, true)]
    public void TryGiveDisease_HolyShieldBlocksAllButStunAndSeduce(PlayerDisease disease, bool expected)
    {
        var player = NewPlayer();
        player.ApplySkillEffect(
            new MapleStatEffect
            {
                SourceId = Player.HolyShieldSkillId,
                Level = 1,
                IsOverTime = true,
                DurationMilliseconds = 60_000,
                Statups = [new BuffStatValue(MapleBuffStat.HOLY_SHIELD, 1)],
            },
            T0);

        Assert.Equal(expected, player.TryGiveDisease(disease, 1, 5000, 0, 1, T0) is not null);
    }

    [Fact]
    public void ExpireDiseases_StrictlyAfterDuration()
    {
        var player = NewPlayer();
        player.TryGiveDisease(PlayerDisease.Stun, 1, 5000, 123, 1, T0);

        Assert.Empty(player.ExpireDiseases(T0.AddSeconds(5)));
        Assert.Single(player.ExpireDiseases(T0.AddSeconds(5.001)));
        Assert.False(player.HasDisease(PlayerDisease.Stun));
    }

    [Fact]
    public void DiseaseFromMobSkill_MapsJavaTable()
    {
        Assert.Equal(PlayerDisease.Stun, Player.DiseaseFromMobSkill(123));
        Assert.Equal(PlayerDisease.Freeze, Player.DiseaseFromMobSkill(137));
        Assert.Null(Player.DiseaseFromMobSkill(127));
    }

    private static Player NewPlayer()
        => new(new Character { Id = 1, Name = "Victim", Stats = new CharacterStats { Hp = 50, MaxHp = 50, Mp = 50, MaxMp = 50 } }, new Position(0, 0, 0, 0));
}
