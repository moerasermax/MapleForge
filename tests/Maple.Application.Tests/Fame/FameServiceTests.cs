using Maple.Application.Fame;
using Maple.Application.OnlinePlayers;
using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Application.Tests.Fame;

public sealed class FameServiceTests
{
    private const long Now = 1_800_000_000_000L;

    [Fact]
    public void GiveFame_IncreasesTargetFameAndRecordsThrottle()
    {
        var registry = new InMemoryOnlinePlayerRegistry();
        var giver = Player(Character(1, "Giver", level: 15, mapId: 100000000));
        var target = Character(2, "Target", level: 20, mapId: 100000000);
        RegisterCharacter(registry, target);
        var service = new FameService(registry);

        var result = service.GiveFame(giver, target.Id, mode: 1, Now);

        Assert.Equal(FameResultStatus.Success, result.Status);
        Assert.Equal(1, target.Fame);
        Assert.Equal(Now, giver.Character.LastFameAtUnixMillis);
        var record = Assert.Single(giver.Character.FameHistory);
        Assert.Equal(target.Id, record.TargetCharacterId);
        Assert.Equal(Now, record.GivenAtUnixMillis);
    }

    [Fact]
    public void GiveFame_DecreasesTargetFameWhenModeIsZero()
    {
        var registry = new InMemoryOnlinePlayerRegistry();
        var giver = Player(Character(1, "Giver", level: 15, mapId: 100000000));
        var target = Character(2, "Target", level: 20, mapId: 100000000);
        RegisterCharacter(registry, target);
        var service = new FameService(registry);

        var result = service.GiveFame(giver, target.Id, mode: 0, Now);

        Assert.Equal(FameResultStatus.Success, result.Status);
        Assert.Equal(-1, target.Fame);
    }

    [Fact]
    public void GiveFame_BlocksSecondGiveOnSameDay()
    {
        var registry = new InMemoryOnlinePlayerRegistry();
        var giver = Player(Character(1, "Giver", level: 15, mapId: 100000000));
        var firstTarget = Character(2, "First", level: 20, mapId: 100000000);
        var secondTarget = Character(3, "Second", level: 20, mapId: 100000000);
        RegisterCharacter(registry, firstTarget);
        RegisterCharacter(registry, secondTarget);
        var service = new FameService(registry);

        var first = service.GiveFame(giver, firstTarget.Id, mode: 1, Now);
        var second = service.GiveFame(giver, secondTarget.Id, mode: 1, Now + 1000);

        Assert.Equal(FameResultStatus.Success, first.Status);
        Assert.Equal(FameResultStatus.AlreadyToday, second.Status);
        Assert.Equal(0, secondTarget.Fame);
    }

    [Fact]
    public void GiveFame_BlocksSameTargetWithinThirtyDays()
    {
        var registry = new InMemoryOnlinePlayerRegistry();
        var giver = Player(Character(1, "Giver", level: 15, mapId: 100000000));
        var target = Character(2, "Target", level: 20, mapId: 100000000);
        giver.Character.LastFameAtUnixMillis = Now - (2L * 24L * 60L * 60L * 1000L);
        giver.Character.FameHistory.Add(new FameRecord
        {
            TargetCharacterId = target.Id,
            GivenAtUnixMillis = giver.Character.LastFameAtUnixMillis,
        });
        RegisterCharacter(registry, target);
        var service = new FameService(registry);

        var result = service.GiveFame(giver, target.Id, mode: 1, Now);

        Assert.Equal(FameResultStatus.AlreadyThisMonth, result.Status);
        Assert.Equal(0, target.Fame);
    }

    [Fact]
    public void GiveFame_PrunesOldTargetRecordAndAllowsAfterThirtyDays()
    {
        var registry = new InMemoryOnlinePlayerRegistry();
        var giver = Player(Character(1, "Giver", level: 15, mapId: 100000000));
        var target = Character(2, "Target", level: 20, mapId: 100000000);
        var old = Now - (31L * 24L * 60L * 60L * 1000L);
        giver.Character.LastFameAtUnixMillis = old;
        giver.Character.FameHistory.Add(new FameRecord
        {
            TargetCharacterId = target.Id,
            GivenAtUnixMillis = old,
        });
        RegisterCharacter(registry, target);
        var service = new FameService(registry);

        var result = service.GiveFame(giver, target.Id, mode: 1, Now);

        Assert.Equal(FameResultStatus.Success, result.Status);
        Assert.Equal(1, target.Fame);
        var record = Assert.Single(giver.Character.FameHistory);
        Assert.Equal(Now, record.GivenAtUnixMillis);
    }

    [Fact]
    public void GiveFame_RequiresLevelFifteen()
    {
        var registry = new InMemoryOnlinePlayerRegistry();
        var giver = Player(Character(1, "Giver", level: 14, mapId: 100000000));
        var target = Character(2, "Target", level: 20, mapId: 100000000);
        RegisterCharacter(registry, target);
        var service = new FameService(registry);

        var result = service.GiveFame(giver, target.Id, mode: 1, Now);

        Assert.Equal(FameResultStatus.UnderLevel, result.Status);
        Assert.Equal(0, target.Fame);
    }

    private static Character Character(int id, string name, byte level, int mapId)
        => new() { Id = id, Name = name, Level = level, MapId = mapId };

    private static Player Player(Character character)
        => new(character, new Position(0, 0, 0, 0));

    private static void RegisterCharacter(InMemoryOnlinePlayerRegistry registry, Character character)
    {
        var player = new Player(character, new Position(0, 0, 0, 0));
        registry.Register(player, 1, SendNoop, new object());
    }

    private static Task SendNoop(byte[] packet, CancellationToken ct)
        => Task.CompletedTask;
}
