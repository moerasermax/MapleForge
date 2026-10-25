using Maple.Application.Drops;
using Maple.Application.Parties;
using Maple.Core.Characters;
using Maple.Core.Drops;
using Maple.Core.Maps;
using Maple.Core.Parties;
using Maple.Core.World;

namespace Maple.Application.Tests.Drops;

/// <summary>P150：怪物經驗依傷害分配（對照 Java MapleMonster.killBy、SingleAttackerEntry、PartyAttackerEntry.killedMob）。</summary>
public sealed class ExpDistributionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SoloKiller_GetsFullExpTimesRate()
    {
        var (drops, field, mob, _) = Setup(expRate: 2);
        var killer = AddPlayer(field, 1, level: 30);
        mob.AddAttackerDamage(1, 100);

        var rewards = drops.OnMobKilled(field, killer, mob);

        Assert.Equal(200, rewards.ExpGained);
        Assert.True(rewards.KillerHighestDamage);
        Assert.Null(rewards.OtherAwards);
    }

    [Fact]
    public void TwoSoloAttackers_SplitByDamageShare_CeilEach()
    {
        var (drops, field, mob, _) = Setup();
        var killer = AddPlayer(field, 1, level: 30);
        var helper = AddPlayer(field, 2, level: 30);
        mob.AddAttackerDamage(2, 61);
        mob.AddAttackerDamage(1, 39);

        var rewards = drops.OnMobKilled(field, killer, mob);

        Assert.Equal(39, rewards.ExpGained);
        Assert.False(rewards.KillerHighestDamage);
        var other = Assert.Single(rewards.OtherAwards!);
        Assert.Same(helper, other.Receiver);
        Assert.Equal(61, other.Exp);
        Assert.True(other.HighestDamage);
    }

    [Fact]
    public void Party_AttackerWeightTwo_MemberWeightPointThree()
    {
        var (drops, field, mob, parties) = Setup();
        var attacker = AddPlayer(field, 1, level: 30);
        var member = AddPlayer(field, 2, level: 30);
        MakeParty(parties, attacker, member);
        mob.AddAttackerDamage(1, 100);

        var rewards = drops.OnMobKilled(field, attacker, mob);

        // expFraction = 100 / (2 + 1) = 33.33；攻擊者 round(66.67) = 67、隊友 round(10.0) = 10
        Assert.Equal(67, rewards.ExpGained);
        Assert.Equal(10, Assert.Single(rewards.OtherAwards!).Exp);
    }

    [Fact]
    public void Party_LowLevelMemberOutsideBothRanges_IsExcluded()
    {
        var (drops, field, mob, parties) = Setup(mobLevel: 30);
        var attacker = AddPlayer(field, 1, level: 70);
        var low = AddPlayer(field, 2, level: 20);
        MakeParty(parties, attacker, low);
        mob.AddAttackerDamage(1, 100);

        var rewards = drops.OnMobKilled(field, attacker, mob);

        // 只剩攻擊者：100 / (1 + 1) * 2.0 = 100
        Assert.Equal(100, rewards.ExpGained);
        Assert.Null(rewards.OtherAwards);
    }

    [Fact]
    public void Party_HigherMemberLevelMod_CappedAtOne_LowerMemberReduced()
    {
        var (drops, field, mob, parties) = Setup(mobLevel: 30);
        var attacker = AddPlayer(field, 1, level: 30);
        var lower = AddPlayer(field, 2, level: 26);
        MakeParty(parties, attacker, lower);
        mob.AddAttackerDamage(1, 100);

        var rewards = drops.OnMobKilled(field, attacker, mob);

        // 平均等級 28；隊友 levelMod = 26/28；round(33.33 * 0.3 * 0.9286) = round(9.29) = 9
        Assert.Equal(9, Assert.Single(rewards.OtherAwards!).Exp);
    }

    [Fact]
    public void Curse_HalvesExp_DeadReceiverGetsNothing()
    {
        var (drops, field, mob, _) = Setup();
        var killer = AddPlayer(field, 1, level: 30);
        var dead = AddPlayer(field, 2, level: 30);
        killer.TryGiveDisease(PlayerDisease.Curse, 1, 60_000, 124, 1, T0);
        dead.Character.Stats.Hp = 0;
        mob.AddAttackerDamage(1, 50);
        mob.AddAttackerDamage(2, 50);

        var rewards = drops.OnMobKilled(field, killer, mob);

        Assert.Equal(25, rewards.ExpGained);
        Assert.Null(rewards.OtherAwards);
    }

    [Fact]
    public void AttackerDamage_CappedAtRemainingHp()
    {
        var mob = MakeMob();
        mob.AddAttackerDamage(1, 999);

        Assert.Equal([(1, 100L)], mob.AttackerDamages);
    }

    private static (DropService, FieldInstance, Mob, InMemoryPartyRegistry) Setup(int expRate = 1, short mobLevel = 30)
    {
        var parties = new InMemoryPartyRegistry();
        var field = new FieldInstance(100000000);
        var mob = MakeMob(mobLevel);
        field.Add(mob);
        return (new DropService(new EmptyDropCatalog(), new DropServiceOptions(ExpRate: expRate, MesoRate: 0), parties: parties), field, mob, parties);
    }

    private static Mob MakeMob(short level = 30)
        => new(new MapMonster { MonsterId = 100100 }, new MobStats(100100, 100, MaxMp: 0, Level: level, Exp: 100), 100001);

    private static Player AddPlayer(FieldInstance field, int id, byte level)
    {
        var player = new Player(new Character { Id = id, Name = $"P{id}", Level = level, MapId = 100000000 }, new Position(0, 0, 0, 0));
        player.Character.Stats.MaxHp = 100;
        player.Character.Stats.Hp = 100;
        field.Add(player);
        return player;
    }

    private static void MakeParty(InMemoryPartyRegistry parties, Player leader, Player member)
    {
        var created = parties.CreateParty(PartyMember.FromCharacter(leader.Character, channelIndex: 1));
        parties.JoinParty(created.Party!.Id, PartyMember.FromCharacter(member.Character, channelIndex: 1));
    }

    private sealed class EmptyDropCatalog : IMonsterDropCatalog
    {
        public IReadOnlyList<MonsterDropEntry> RetrieveDrop(int monsterId) => [];
    }
}
