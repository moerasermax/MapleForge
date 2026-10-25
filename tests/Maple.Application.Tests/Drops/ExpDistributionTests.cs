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
        // P151：2 人組隊經驗 (5 * (2 * (3 + 3 / 2)) - 20) / 100 = 20% → 67 + 13、10 + 2
        Assert.Equal(67 + 13, rewards.ExpGained);
        Assert.Equal(67, rewards.KillerAward!.ShownExp);
        Assert.Equal(13, rewards.KillerAward.PartyExp);
        Assert.Equal(10 + 2, Assert.Single(rewards.OtherAwards!).Exp);
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

        // 平均等級 28；隊友 levelMod = 26/28；round(33.33 * 0.3 * 0.9286) = round(9.29) = 9；組隊 20% → 9 + 1
        Assert.Equal(9, Assert.Single(rewards.OtherAwards!).ShownExp);
        Assert.Equal(10, rewards.OtherAwards![0].Exp);
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

    [Theory]
    [InlineData((short)2100, 5 * 100)] // 狂狼 10 級以下 5 倍（不看伺服器倍率）
    [InlineData((short)1000, 5 * 100)] // 皇家騎士團
    [InlineData((short)0, 100)]
    public void BelowLevelTen_UsesJobRateNotServerRate(short job, int expected)
    {
        var (drops, field, mob, _) = Setup(expRate: 3);
        var killer = AddPlayer(field, 1, level: 9);
        killer.Character.Job = job;
        mob.AddAttackerDamage(1, 100);

        Assert.Equal(expected, drops.OnMobKilled(field, killer, mob).ExpGained);
    }

    [Fact]
    public void HolySymbolSoloPriest_ShowdownAndExpBuff_AddAdditionalExp()
    {
        var (drops, field, mob, _) = Setup();
        var killer = AddPlayer(field, 1, level: 30);
        killer.ApplyBuffStats(Buff(DropService.HolySymbolPriestSkillId, Maple.Core.Skills.MapleBuffStat.HOLY_SYMBOL, 50), T0);
        mob.TryApplyStatus(new MonsterStatusEffect(MonsterStatus.Showdown, 10, 4121003, null, null));
        mob.AddAttackerDamage(1, 100);

        var rewards = drops.OnMobKilled(field, killer, mob);

        // additional = (int)((int)(100 * 1.1) * (1 + 50 / 500.0)) = (int)(110 * 1.1) = 121 → 121 - 100 = 21
        Assert.Equal(121, rewards.ExpGained);
        Assert.Equal(121, rewards.KillerAward!.ShownExp);
    }

    [Fact]
    public void ExpRateBuff_DoublesViaIntTruncatedMultiplier()
    {
        var (drops, field, mob, _) = Setup();
        var killer = AddPlayer(field, 1, level: 30);
        killer.ApplyBuffStats(Buff(2450000, Maple.Core.Skills.MapleBuffStat.EXPRATE, 200), T0);
        mob.AddAttackerDamage(1, 100);

        Assert.Equal(200, drops.OnMobKilled(field, killer, mob).ExpGained);

        var (drops2, field2, mob2, _) = Setup();
        var killer2 = AddPlayer(field2, 1, level: 30);
        killer2.ApplyBuffStats(Buff(2450000, Maple.Core.Skills.MapleBuffStat.EXPRATE, 150), T0);
        mob2.AddAttackerDamage(1, 100);

        Assert.Equal(100, drops2.OnMobKilled(field2, killer2, mob2).ExpGained); // (int)(150/100) = 1
    }

    private static Maple.Core.Skills.MapleStatEffect Buff(int sourceId, Maple.Core.Skills.MapleBuffStat stat, int value)
        => new()
        {
            SourceId = sourceId,
            Level = 1,
            IsOverTime = true,
            DurationMilliseconds = 60_000,
            Statups = [new Maple.Core.Skills.BuffStatValue(stat, value)],
        };

    [Fact]
    public void Drops_BelongToHighestDamageDealer()
    {
        var parties = new InMemoryPartyRegistry();
        var field = new FieldInstance(100000000);
        var mob = MakeMob();
        field.Add(mob);
        var drops = new DropService(new EmptyDropCatalog(), new DropServiceOptions(), parties: parties);
        var killer = AddPlayer(field, 1, level: 30);
        AddPlayer(field, 2, level: 30);
        mob.AddAttackerDamage(2, 70);
        mob.AddAttackerDamage(1, 30);

        var meso = Assert.Single(drops.OnMobKilled(field, killer, mob).SpawnedDrops);

        Assert.Equal(2, meso.OwnerId);
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
