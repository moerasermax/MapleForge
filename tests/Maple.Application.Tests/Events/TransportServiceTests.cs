using Maple.Application.Events;
using Maple.Application.Maps;

namespace Maple.Application.Tests.Events;

/// <summary>P173：交通船班（對照 scripts/event/Boats.js、Trains.js、AirPlane.js、Subway.js）。</summary>
public sealed class TransportServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Boats_EntryCloses_Takeoff_BalrogInvasion_Arrival()
    {
        var (service, transfer, effects) = Setup(randomDouble: 0.95); // chance 9 → 2 隻巴洛古
        await service.StartAsync(T0, default);

        Assert.Equal("true", service.GetProperty("Boats", "entry"));
        Assert.True(service.IsDocked(101000300));
        Assert.Contains((101000300, true), effects.Boat);

        await service.TickAsync(T0.AddSeconds(120), default);
        Assert.Equal("false", service.GetProperty("Boats", "entry"));
        Assert.Equal("false", service.GetProperty("Boats", "docked"));
        Assert.Contains((200000112, 200090000), transfer.Warps); // 天空之城候船室 → 往魔法森林的船
        Assert.Contains((101000301, 200090010), transfer.Warps);
        Assert.False(service.IsDocked(101000300));

        await service.TickAsync(T0.AddSeconds(180), default);
        Assert.Equal(4, effects.Spawns.Count);
        Assert.Equal("true", service.GetProperty("Boats", "haveBalrog"));
        Assert.Contains((200090010, 1034), effects.BoatEffects);
        Assert.True(service.IsDocked(200090010));

        await service.TickAsync(T0.AddSeconds(720), default);
        Assert.Contains((200090011, 200000100), transfer.Warps); // 船艙 → 天空之城
        Assert.Equal([200090010, 200090000], effects.Killed);
        Assert.Equal("true", service.GetProperty("Boats", "entry"));
        Assert.Equal("false", service.GetProperty("Boats", "haveBalrog"));
    }

    [Fact]
    public async Task Trains_AndAirPlane_FollowScriptTimings()
    {
        var (service, transfer, _) = Setup(randomDouble: 0);
        await service.StartAsync(T0, default);
        Assert.Null(service.GetProperty("AirPlane", "entry")); // Java 一開始沒設

        await service.TickAsync(T0.AddSeconds(240), default);
        Assert.Equal("false", service.GetProperty("Trains", "entry"));
        Assert.Equal("false", service.GetProperty("AirPlane", "entry"));

        await service.TickAsync(T0.AddSeconds(300), default);
        Assert.Contains((200000122, 200090100), transfer.Warps); // 天空之城 → 往玩具城的列車
        Assert.Equal("true", service.GetProperty("AirPlane", "entry"));

        await service.TickAsync(T0.AddSeconds(900), default);
        Assert.Contains((200090100, 220000100), transfer.Warps);
        Assert.Equal("true", service.GetProperty("Trains", "entry"));
    }

    [Fact]
    public async Task Subway_SendsNoticesAndClock()
    {
        var (service, _, effects) = Setup(randomDouble: 0);
        await service.StartAsync(T0, default);
        Assert.Contains((103000100, 6, "地鐵列車到站了。"), effects.Notices);

        await service.TickAsync(T0.AddSeconds(120), default);
        Assert.Contains((600010003, 60), effects.Clocks);
        Assert.Contains((600010001, 6, "地鐵列車已經離開了."), effects.Notices);
        Assert.Equal("OBoat1", TransportService.GetPortalScriptOverride(200090011, "out00"));
    }

    private static (TransportService, FakeTransfer, FakeEffects) Setup(double randomDouble)
    {
        var transfer = new FakeTransfer();
        var effects = new FakeEffects();
        return (new TransportService(transfer, effects, new FixedRandom(randomDouble)), transfer, effects);
    }

    private sealed class FakeTransfer : IPlayerTransferService
    {
        public List<(int From, int To)> Warps { get; } = [];

        public bool Warp(int characterId, int mapId, int portal) => true;

        public int WarpAll(int fromMapId, int toMapId, int portal)
        {
            Warps.Add((fromMapId, toMapId));
            return 0;
        }
    }

    private sealed class FakeEffects : ITransportEffects
    {
        public List<(int, bool)> Boat { get; } = [];
        public List<(int, int)> BoatEffects { get; } = [];
        public List<(int, int, string)> Notices { get; } = [];
        public List<(int, int)> Clocks { get; } = [];
        public List<(int, int)> Spawns { get; } = [];
        public List<int> Killed { get; } = [];

        public Task BoatPacketAsync(int mapId, bool docked, CancellationToken ct) { Boat.Add((mapId, docked)); return Task.CompletedTask; }
        public Task BoatEffectAsync(int mapId, int effect, CancellationToken ct) { BoatEffects.Add((mapId, effect)); return Task.CompletedTask; }
        public Task MusicAsync(int mapId, string song, CancellationToken ct) => Task.CompletedTask;
        public Task NoticeAsync(int mapId, int type, string message, CancellationToken ct) { Notices.Add((mapId, type, message)); return Task.CompletedTask; }
        public Task ClockAsync(int mapId, int seconds, CancellationToken ct) { Clocks.Add((mapId, seconds)); return Task.CompletedTask; }
        public Task SpawnMonsterAsync(int mapId, int monsterId, short x, short y, CancellationToken ct) { Spawns.Add((mapId, monsterId)); return Task.CompletedTask; }
        public Task KillAllMonstersAsync(int mapId, CancellationToken ct, bool animate = false) { Killed.Add(mapId); return Task.CompletedTask; }
    }

    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }
}
