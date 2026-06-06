using Maple.Application.Stats;
using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Application.Tests.Stats;

public sealed class StatsServiceTests
{
    [Fact]
    public void RecoverOverTime_UsesInjectedClock()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 6, 1, 0, 0, TimeSpan.Zero));
        var service = new StatsService(clock, RollMin);
        var player = MakePlayer(hp: 10, maxHp: 20, mp: 1, maxMp: 5);

        var first = service.RecoverOverTime(player, 5, 2);
        clock.UtcNow = clock.UtcNow.AddMilliseconds(500);
        var second = service.RecoverOverTime(player, 5, 2);

        Assert.True(first.Applied);
        Assert.Equal(PlayerStatsFailure.NoChange, second.Failure);
        Assert.Equal((short)15, player.Hp);
        Assert.Equal((short)3, player.Mp);
    }

    [Fact]
    public void GainExperience_UsesInjectedRoller()
    {
        var service = new StatsService(TimeProvider.System, RollMin);
        var player = MakePlayer(level: 1, exp: 14, job: 0);

        service.GainExperience(player, 1);

        Assert.Equal((byte)2, player.Character.Level);
        Assert.Equal((short)62, player.MaxHp);
        Assert.Equal((short)15, player.MaxMp);
    }

    private static int RollMin(int min, int max) => min;

    private static Player MakePlayer(
        byte level = 1,
        int exp = 0,
        short job = 0,
        short hp = 50,
        short maxHp = 50,
        short mp = 5,
        short maxMp = 5)
    {
        var chr = new Character
        {
            Id = 1,
            Name = "Tester",
            Level = level,
            Job = job,
            Exp = exp,
            Stats = new CharacterStats
            {
                Str = 12,
                Dex = 5,
                Int = 4,
                Luk = 4,
                Hp = hp,
                MaxHp = maxHp,
                Mp = mp,
                MaxMp = maxMp,
            },
        };

        return new Player(chr, new Position(0, 0, 0, 0));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
