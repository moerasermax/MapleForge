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

    [Fact]
    public void RemoveForCancelledBuffs_SummonOrPuppetStat_RemovesMatchingOwnerSummonOnly()
    {
        // P082：對照 Java deregisterBuffStats 的 SUMMON/PUPPET 分支。
        var field = new FieldInstance(100000000);
        var service = new SummonService();
        var effect = new MapleStatEffect { X = 10 };
        var hawk = service.TrySpawn(field, NewPlayer(7), 3111005, 1, effect, Pos)!.Summon;
        var octopus = service.TrySpawn(field, NewPlayer(7), 5211001, 1, effect, Pos)!.Summon;
        var othersHawk = service.TrySpawn(field, NewPlayer(8), 3111005, 1, effect, Pos)!.Summon;

        var removed = service.RemoveForCancelledBuffs(field, ownerId: 7, new[]
        {
            new PlayerBuffCancellation(3111005, new[] { MapleBuffStat.SUMMON }),
            new PlayerBuffCancellation(2001002, new[] { MapleBuffStat.MAGIC_GUARD }),
        });

        Assert.Equal(new[] { hawk }, removed);
        Assert.Null(field.Get(hawk.ObjectId));
        Assert.Same(octopus, field.Get(octopus.ObjectId));
        Assert.Same(othersHawk, field.Get(othersHawk.ObjectId));
    }

    [Fact]
    public void RemoveForCancelledBuffs_PuppetStat_RemovesPuppet()
    {
        var field = new FieldInstance(100000000);
        var service = new SummonService();
        var puppet = service.TrySpawn(field, NewPlayer(7), 3111002, 1, new MapleStatEffect { X = 100 }, Pos)!.Summon;

        var removed = service.RemoveForCancelledBuffs(field, 7, new[] { new PlayerBuffCancellation(3111002, new[] { MapleBuffStat.PUPPET }) });

        Assert.Equal(new[] { puppet }, removed);
    }

    [Fact]
    public void RemoveForCancelledBuffs_NonSummonStats_RemovesNothing()
    {
        var field = new FieldInstance(100000000);
        var service = new SummonService();
        var hawk = service.TrySpawn(field, NewPlayer(7), 3111005, 1, new MapleStatEffect { X = 10 }, Pos)!.Summon;

        // 同一來源 ID 但沒有 SUMMON/PUPPET stat（Java 只在這兩個 stat 分支移除召喚獸）。
        var removed = service.RemoveForCancelledBuffs(field, 7, new[] { new PlayerBuffCancellation(3111005, new[] { MapleBuffStat.WATK }) });

        Assert.Empty(removed);
        Assert.Same(hawk, field.Get(hawk.ObjectId));
    }

    [Fact]
    public void DetachOwnerSummons_SplitsStationaryAndPuppetFromFollowers()
    {
        // P083：對照 Java MapleMap.removePlayer——原地型/傀儡取消 buff，跟隨型靜默移出帶走。
        var field = new FieldInstance(100000000);
        var service = new SummonService();
        var effect = new MapleStatEffect { X = 10 };
        var octopus = service.TrySpawn(field, NewPlayer(7), 5211001, 1, effect, Pos)!.Summon;   // Stationary
        var gull = service.TrySpawn(field, NewPlayer(7), 5211002, 1, effect, Pos)!.Summon;      // CircleStationary
        var puppet = service.TrySpawn(field, NewPlayer(7), 3111002, 1, effect, Pos)!.Summon;    // Puppet
        var dragon = service.TrySpawn(field, NewPlayer(7), 2311006, 1, effect, Pos)!.Summon;    // CircleFollow
        var ifrit = service.TrySpawn(field, NewPlayer(7), 2221005, 1, effect, Pos)!.Summon;     // Follow
        var others = service.TrySpawn(field, NewPlayer(8), 2311006, 1, effect, Pos)!.Summon;

        var detached = service.DetachOwnerSummons(field, ownerId: 7);

        Assert.Equal(new[] { octopus, gull, puppet }.OrderBy(s => s.ObjectId), detached.Cancelled.OrderBy(s => s.ObjectId));
        Assert.Equal(new[] { dragon, ifrit }.OrderBy(s => s.ObjectId), detached.Carried.OrderBy(s => s.ObjectId));
        Assert.Equal(new[] { others }, field.Objects.OfType<Summon>());
    }

    [Fact]
    public void Reattach_SummonBuffStillActive_SpawnsInNewFieldAtPosition()
    {
        // P085：對照 Java MapleMap.addPlayer 的 getStatForBuff(SUMMON) 分支。
        var owner = NewPlayer(7);
        var dragonEffect = new MapleStatEffect
        {
            SourceId = 2311006,
            Level = 1,
            X = 40,
            IsOverTime = true,
            DurationMilliseconds = 60_000,
            Statups = new[] { new BuffStatValue(MapleBuffStat.SUMMON, 1) },
        };
        owner.ApplySkillEffect(dragonEffect, DateTimeOffset.UtcNow);
        var service = new SummonService();
        var oldField = new FieldInstance(100000000);
        var dragon = service.TrySpawn(oldField, owner, 2311006, 1, dragonEffect, Pos)!.Summon;
        var carried = service.DetachOwnerSummons(oldField, 7).Carried.Single();
        var newField = new FieldInstance(101000000);
        var landing = new Position(-300, 50, 0, 0);

        var reattached = service.Reattach(newField, owner, carried, landing);

        Assert.NotNull(reattached);
        Assert.Same(reattached, newField.Get(reattached!.ObjectId));
        Assert.Equal((2311006, (short)40, SummonMovementType.CircleFollow, landing), (reattached.SkillId, reattached.Hp, reattached.MovementType, reattached.Position));
        Assert.Same(dragon, carried);
    }

    [Fact]
    public void Reattach_SummonBuffGone_ReturnsNull()
    {
        var owner = NewPlayer(7);
        var service = new SummonService();
        var oldField = new FieldInstance(100000000);
        service.TrySpawn(oldField, owner, 2311006, 1, new MapleStatEffect { X = 40 }, Pos);
        var carried = service.DetachOwnerSummons(oldField, 7).Carried.Single();
        var newField = new FieldInstance(101000000);

        Assert.Null(service.Reattach(newField, owner, carried, Pos));
        Assert.Empty(newField.Objects);
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
