using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>P116：MAXHP buff 結束時壓回上限並送 UPDATE_STATS（Java cancelEffect → recalcLocalStats → enforceMaxHpMp）。</summary>
public sealed class ChannelBuffCancellationEnforceTests
{
    [Fact]
    public async Task ApplyAsync_HyperBodyEnds_SendsClampedHp()
    {
        var effects = new V113BuffCancellationEffects(new SummonService(), new DoorService(), new InMemoryMapSessionRegistry());
        var player = new Player(
            new Character { Id = 1, Name = "Tank", Stats = new CharacterStats { Hp = 1, MaxHp = 1000, Mp = 1, MaxMp = 100 } },
            new Position(0, 0, 0, 0));
        player.ApplySkillEffect(
            new MapleStatEffect
            {
                SourceId = 1301007,
                Level = 1,
                IsOverTime = true,
                DurationMilliseconds = 60_000,
                Statups = [new BuffStatValue(MapleBuffStat.MAXHP, 60)],
            },
            DateTimeOffset.UnixEpoch);
        player.HealHp(5000);
        var cancellations = player.CancelBuffBySource(1301007);
        var sent = new List<byte[]>();

        await effects.ApplyAsync(player, field: null, cancellations, CancellationToken.None, (pkt, _) => { sent.Add(pkt); return Task.CompletedTask; });

        Assert.Equal((short)1000, player.Hp);
        Assert.Equal(
            V113StatsPackets.UpdateStats([new PlayerStatUpdate(PlayerStatKind.Hp, 1000)]),
            Assert.Single(sent));
    }
}
