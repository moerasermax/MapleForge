using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Skills;

/// <summary>P157：騎寵（Java applyBuffEffect isMonsterRiding、parseMountInfo／parseMountInfo_Pure、getMountItem）。</summary>
public sealed class MonsterRidingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MonsterRiding_WithMountAndSaddle_RegistersLongBuff_AndCancelsPowerGuard()
    {
        var (service, player) = Setup(1004);
        player.Character.Equips.Add(new EquipEntry { Position = -18, ItemId = 1902000 });
        player.Character.Equips.Add(new EquipEntry { Position = -19, ItemId = 1912000 });
        player.ApplyBuffStats(Buff(1101007, MapleBuffStat.POWERGUARD), T0);

        var result = service.Cast(player, 1004, 1, T0);

        Assert.Equal(new MountInfo(1902000, 1902000), result.Mount);
        Assert.Equal(SkillService.MountDurationMilliseconds, result.AppliedBuff!.DurationMilliseconds);
        Assert.Equal(1, player.GetBuffValue(MapleBuffStat.MONSTER_RIDING));
        Assert.Equal(1101007, Assert.Single(result.CancelledBuffs!).SourceId);
        Assert.Null(player.GetBuffValue(MapleBuffStat.POWERGUARD));
    }

    [Fact]
    public void MonsterRiding_WithoutSaddle_GivesNoBuff()
    {
        var (service, player) = Setup(1004);
        player.Character.Equips.Add(new EquipEntry { Position = -18, ItemId = 1902000 });

        var result = service.Cast(player, 1004, 1, T0);

        Assert.Equal(SkillCastStatus.Success, result.Status);
        Assert.Null(result.AppliedBuff);
        Assert.Null(result.Mount);
        Assert.Null(player.GetBuffValue(MapleBuffStat.MONSTER_RIDING));
    }

    [Fact]
    public void CashMount_IsUsedForForeignAppearance()
    {
        var (_, player) = Setup(1004);
        player.Character.Equips.Add(new EquipEntry { Position = -18, ItemId = 1902000 });
        player.Character.Equips.Add(new EquipEntry { Position = -19, ItemId = 1912000 });
        player.Character.Equips.Add(new EquipEntry { Position = -118, ItemId = 1902040 });
        player.Character.Equips.Add(new EquipEntry { Position = -119, ItemId = 1912030 });

        Assert.Equal(1902040, SkillService.ParseMountInfo(player, 1004));
        Assert.Equal(1902000, SkillService.ParseMountInfoPure(player, 1004));
    }

    [Theory]
    [InlineData(5221006, 1932000)]
    [InlineData(1013, 1932001)]
    [InlineData(20001019, 1932003)]
    [InlineData(2001002, 0)]
    public void GetMountItem_MatchesJava(int skillId, int expected) => Assert.Equal(expected, SkillService.GetMountItem(skillId));

    [Fact]
    public void BattleshipRiding_NeedsNoEquipment()
    {
        var (service, player) = Setup(5221006);

        var result = service.Cast(player, 5221006, 1, T0);

        Assert.Equal(new MountInfo(1932000, 1932000), result.Mount);
    }

    private static MapleStatEffect Buff(int id, MapleBuffStat stat)
        => new() { SourceId = id, Level = 1, IsOverTime = true, DurationMilliseconds = 60_000, Statups = [new BuffStatValue(stat, 10)] };

    private static (SkillService, Player) Setup(int skillId)
    {
        var skill = new MapleSkill
        {
            Id = skillId,
            Effects =
            [
                new MapleStatEffect
                {
                    SourceId = skillId,
                    Level = 1,
                    IsOverTime = true,
                    DurationMilliseconds = 60_000,
                    Statups = [new BuffStatValue(MapleBuffStat.MONSTER_RIDING, 1)],
                },
            ],
        };
        var player = new Player(new Character { Id = 1, Name = "Rider" }, new Position(0, 0, 0, 0));
        player.ChangeSkillLevel(skillId, 1, 1);
        return (new SkillService(new InMemorySkillCatalog([skill])), player);
    }
}
