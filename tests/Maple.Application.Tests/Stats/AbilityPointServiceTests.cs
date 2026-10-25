using Maple.Application.Skills;
using Maple.Application.Stats;
using Maple.Core.Characters;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Stats;

/// <summary>P162：AP 投 HP/MP 與 AP 重設（對照 Java StatsHandling.DistributeAP、InventoryHandler 5050000）。</summary>
public sealed class AbilityPointServiceTests
{
    [Fact]
    public void DistributeMaxHp_Warrior_AddsRollPlusImprovingHpX()
    {
        var player = MakePlayer(job: 110);
        player.ChangeSkillLevel(1000001, 1, 10);
        var service = new AbilityPointService(Catalog(1000001, x: 7, y: 3), new MinRandom());

        var mutation = service.DistributeHpMp(player, AbilityPointTarget.MaxHp);

        Assert.Equal((short)(500 + 20 + 7), player.Character.Stats.MaxHp); // 配點用 x
        Assert.Equal(1, player.Character.HpMpApUsed);
        Assert.Equal(4, player.Character.RemainingAp);
        Assert.Equal(
            [new PlayerStatUpdate(PlayerStatKind.MaxHp, 527), new PlayerStatUpdate(PlayerStatKind.AvailableAp, 4)],
            mutation.Updates);
    }

    [Fact]
    public void DistributeMaxMp_Magician_AddsTwiceImprovingMpY_AndRespectsCaps()
    {
        var player = MakePlayer(job: 210);
        player.ChangeSkillLevel(2000001, 1, 10);
        var service = new AbilityPointService(Catalog(2000001, x: 0, y: 4), new MinRandom());

        service.DistributeHpMp(player, AbilityPointTarget.MaxMp);
        Assert.Equal((short)(300 + 18 + 8), player.Character.Stats.MaxMp);

        player.Character.Stats.MaxMp = 30000;
        Assert.False(service.DistributeHpMp(player, AbilityPointTarget.MaxMp).Applied);
        player.Character.RemainingAp = 0;
        Assert.False(service.DistributeHpMp(player, AbilityPointTarget.MaxHp).Applied);
    }

    [Theory]
    [InlineData(110, AbilityPointTarget.Str, 35, false)] // 劍士 STR ≤ 35
    [InlineData(110, AbilityPointTarget.Str, 36, true)]
    [InlineData(310, AbilityPointTarget.Dex, 25, false)]
    [InlineData(510, AbilityPointTarget.Dex, 21, true)]
    [InlineData(210, AbilityPointTarget.Int, 20, false)]
    [InlineData(0, AbilityPointTarget.Luk, 4, false)]
    public void ResetAp_FromThresholds_MatchJava(int job, AbilityPointTarget from, short value, bool used)
    {
        var player = MakePlayer(job);
        var stats = player.Character.Stats;
        (stats.Str, stats.Dex, stats.Int, stats.Luk) = (50, 50, 50, 50);
        switch (from)
        {
            case AbilityPointTarget.Str: stats.Str = value; break;
            case AbilityPointTarget.Dex: stats.Dex = value; break;
            case AbilityPointTarget.Int: stats.Int = value; break;
            default: stats.Luk = value; break;
        }

        var to = from == AbilityPointTarget.Luk ? AbilityPointTarget.Str : AbilityPointTarget.Luk;
        Assert.Equal(used, new AbilityPointService(random: new MinRandom()).ResetAp(player, to, from).Used);
    }

    [Fact]
    public void ResetAp_MaxHpToMaxMp_RequiresHpMpApUsed_AndSendsMaxHpThenHp()
    {
        var player = MakePlayer(job: 0);
        var service = new AbilityPointService(random: new MinRandom());
        Assert.False(service.ResetAp(player, AbilityPointTarget.MaxMp, AbilityPointTarget.MaxHp).Used); // hpmpApUsed == 0

        player.Character.HpMpApUsed = 3;
        var result = service.ResetAp(player, AbilityPointTarget.MaxMp, AbilityPointTarget.MaxHp);

        Assert.True(result.Used);
        Assert.Equal(
            [
                new PlayerStatUpdate(PlayerStatKind.MaxMp, 306),
                new PlayerStatUpdate(PlayerStatKind.MaxHp, 491),
                new PlayerStatUpdate(PlayerStatKind.Hp, 400),
            ],
            result.Updates);
        Assert.Equal(4, player.Character.HpMpApUsed); // to +1；from 不回減（Java 註解掉）
    }

    [Fact]
    public void ResetAp_SameStatOrMpToNonHp_NotUsed()
    {
        var player = MakePlayer(job: 0);
        player.Character.HpMpApUsed = 3;
        var service = new AbilityPointService(random: new MinRandom());

        Assert.False(service.ResetAp(player, AbilityPointTarget.Str, AbilityPointTarget.Str).Used);
        var mp = service.ResetAp(player, AbilityPointTarget.Str, AbilityPointTarget.MaxMp);
        Assert.True(mp.MpToNonHp);
        Assert.False(mp.Used);
    }

    private static Player MakePlayer(int job)
    {
        var player = new Player(new Character { Id = 1, Name = "Ap", Job = (short)job, RemainingAp = 5 }, new Position(0, 0, 0, 0));
        var stats = player.Character.Stats;
        (stats.MaxHp, stats.Hp, stats.MaxMp, stats.Mp) = (500, 400, 300, 200);
        (stats.Str, stats.Dex, stats.Int, stats.Luk) = (50, 50, 50, 50);
        return player;
    }

    private static InMemorySkillCatalog Catalog(int skillId, int x, int y)
        => new([new MapleSkill { Id = skillId, Effects = [new MapleStatEffect { SourceId = skillId, Level = 1, X = x, Y = y }] }]);

    private sealed class MinRandom : Random
    {
        public override int Next(int minValue, int maxValue) => minValue;
    }
}
