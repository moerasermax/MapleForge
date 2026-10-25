using Maple.Application.Parties;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Parties;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Skills;

/// <summary>P145：隊伍 buff（對照 Java MapleStatEffect.applyBuff 隊伍分支、applyTo primary=false）。</summary>
public sealed class PartyBuffServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly MobSkillArea Area = new(-200, -100, 200, 100);

    [Fact]
    public void PartyMemberInRange_GetsBuff_OthersDoNot()
    {
        var (service, field, caster, member, stranger, far) = Setup();

        var applied = service.Apply(field, caster, Haste(), facingLeft: true, T0);

        var only = Assert.Single(applied);
        Assert.Same(member, only.Target);
        Assert.Equal(10, member.GetBuffValue(MapleBuffStat.SPEED));
        Assert.Null(stranger.GetBuffValue(MapleBuffStat.SPEED));
        Assert.Null(far.GetBuffValue(MapleBuffStat.SPEED));
        Assert.Null(caster.GetBuffValue(MapleBuffStat.SPEED)); // 施放者本人由 Cast 處理
        Assert.Equal([new PlayerStatUpdate(PlayerStatKind.Hp, member.Hp)], only.StatUpdates);
    }

    [Fact]
    public void NoParty_NonGmBuff_AppliesToNobody()
    {
        var (service, field, caster, _, _, _) = Setup(withParty: false);

        Assert.Empty(service.Apply(field, caster, Haste(), true, T0));
    }

    [Fact]
    public void GmBuff_AppliesToEveryoneInRange_WithoutParty()
    {
        var (service, field, caster, member, stranger, far) = Setup(withParty: false);
        var echo = Haste(sourceId: 1005, stat: MapleBuffStat.ECHO_OF_HERO);

        var targets = service.Apply(field, caster, echo, true, T0).Select(static a => a.Target).ToArray();

        Assert.Contains(member, targets);
        Assert.Contains(stranger, targets);
        Assert.DoesNotContain(far, targets);
    }

    [Fact]
    public void ChargeOrNoArea_IsNotPartyBuff()
    {
        var (service, field, caster, _, _, _) = Setup();

        Assert.Empty(service.Apply(field, caster, Haste(sourceId: 1211004), true, T0));
        var noArea = new MapleStatEffect { SourceId = 4101004, Level = 1, IsOverTime = true, Statups = [new BuffStatValue(MapleBuffStat.SPEED, 10)] };
        Assert.Empty(service.Apply(field, caster, noArea, true, T0));
        Assert.False(PartyBuffService.IsPartyBuff(new MapleStatEffect { SourceId = 4101004 }));
    }

    [Fact]
    public void HpRate_UsesCasterMaxHp()
    {
        var (service, field, caster, member, _, _) = Setup();
        caster.Character.Stats.MaxHp = 2000;
        member.Character.Stats.Hp = 100;
        var heal = new MapleStatEffect { SourceId = 2301004, Level = 1, IsOverTime = true, HpRate = 0.1, Area = Area };

        service.Apply(field, caster, heal, true, T0);

        Assert.Equal((short)300, member.Hp); // 2000 × 10%
    }

    [Fact]
    public void Resurrection_RevivesDeadMembersOnly()
    {
        var (service, field, caster, member, _, _) = Setup();
        member.Character.Stats.Hp = 0;
        var resurrection = new MapleStatEffect { SourceId = 2321006, Level = 1, IsOverTime = true, Area = Area };

        var applied = Assert.Single(service.Apply(field, caster, resurrection, true, T0));

        Assert.Equal((short)1000, member.Hp);
        Assert.Same(member, applied.Target);
        Assert.Empty(service.Apply(field, caster, resurrection, true, T0)); // 已復活
    }

    [Fact]
    public void Dispel_CuresMemberDiseases()
    {
        var (service, field, caster, member, _, _) = Setup();
        member.TryGiveDisease(PlayerDisease.Weaken, 1, 60_000, 122, 1, T0);
        var dispel = new MapleStatEffect { SourceId = 2311001, Level = 1, IsOverTime = true, Prop = 100, Area = Area };

        var applied = Assert.Single(service.Apply(field, caster, dispel, true, T0));

        Assert.Equal([PlayerDisease.Weaken], applied.CuredDiseases);
        Assert.False(member.HasDisease(PlayerDisease.Weaken));
    }

    [Fact]
    public void TimeLeap_ResetsMemberCooldowns()
    {
        var (service, field, caster, member, _, _) = Setup();
        member.AddSkillCooldown(1121008, T0, 30);
        var timeLeap = new MapleStatEffect { SourceId = SkillService.TimeLeapSkillId, Level = 1, IsOverTime = true, Area = Area };

        var applied = Assert.Single(service.Apply(field, caster, timeLeap, true, T0));

        Assert.Equal([1121008], applied.ResetCooldownSkillIds);
        Assert.False(member.SkillIsCooling(1121008, T0));
    }

    [Theory]
    [InlineData(5110001, false)]
    [InlineData(2301002, true)]
    public void ShouldApplyBuff_MatchesJavaCondition(int skillId, bool expected)
        => Assert.Equal(expected, PartyBuffService.ShouldApplyBuff(new MapleStatEffect { SourceId = skillId, IsOverTime = skillId == 5110001 }));

    private static MapleStatEffect Haste(int sourceId = 4101004, MapleBuffStat stat = MapleBuffStat.SPEED)
        => new()
        {
            SourceId = sourceId,
            Level = 1,
            IsOverTime = true,
            DurationMilliseconds = 60_000,
            Statups = [new BuffStatValue(stat, 10)],
            Area = Area,
        };

    private static (PartyBuffService, FieldInstance, Player Caster, Player Member, Player Stranger, Player Far) Setup(bool withParty = true)
    {
        var parties = new InMemoryPartyRegistry();
        var field = new FieldInstance(100000000);
        var caster = MakePlayer(1, 0);
        var member = MakePlayer(2, 50);
        var stranger = MakePlayer(3, -50);
        var far = MakePlayer(4, 900);
        if (withParty)
        {
            var created = parties.CreateParty(PartyMember.FromCharacter(caster.Character, channelIndex: 1));
            parties.JoinParty(created.Party!.Id, PartyMember.FromCharacter(member.Character, channelIndex: 1));
            parties.JoinParty(created.Party!.Id, PartyMember.FromCharacter(far.Character, channelIndex: 1));
        }

        foreach (var player in new[] { caster, member, stranger, far })
        {
            field.Add(player);
        }

        return (new PartyBuffService(parties), field, caster, member, stranger, far);
    }

    private static Player MakePlayer(int id, short x)
    {
        var player = new Player(new Character { Id = id, Name = $"P{id}", MapId = 100000000 }, new Position(x, 0, 0, 0));
        player.Character.Stats.MaxHp = 1000;
        player.Character.Stats.Hp = 500;
        return player;
    }
}
