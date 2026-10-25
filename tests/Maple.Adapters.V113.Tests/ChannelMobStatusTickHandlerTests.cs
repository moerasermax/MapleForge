using Maple.Adapters.V113.Channel;
using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Core.Data;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>P112：怪物異常狀態到期解除並廣播 CANCEL_MONSTER_STATUS（對照 Java applyStatus 的 cancelTask）。</summary>
public sealed class ChannelMobStatusTickHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TickAsync_ExpiredStatus_RemovesAndBroadcastsCancel()
    {
        var (handler, field, mob, received) = Setup();
        mob.TryApplyStatus(new MonsterStatusEffect(MonsterStatus.Stun, 1, 1111005, null, T0.AddSeconds(2)));
        mob.TryApplyStatus(new MonsterStatusEffect(MonsterStatus.Speed, -20, 2101003, null, T0.AddSeconds(30)));

        await handler.TickAsync(field, T0.AddSeconds(2), CancellationToken.None);

        Assert.False(mob.Statuses.ContainsKey(MonsterStatus.Stun));
        Assert.True(mob.Statuses.ContainsKey(MonsterStatus.Speed));
        var packet = Assert.Single(received);
        Assert.Equal(V113MonsterStatusCodec.CancelMonsterStatus(mob.ObjectId, [MonsterStatus.Stun]), packet);
    }

    [Fact]
    public async Task TickAsync_NothingExpired_DoesNotBroadcast()
    {
        var (handler, field, mob, received) = Setup();
        mob.TryApplyStatus(new MonsterStatusEffect(MonsterStatus.Stun, 1, 1111005, null, T0.AddSeconds(5)));

        await handler.TickAsync(field, T0.AddSeconds(4), CancellationToken.None);

        Assert.Empty(received);
        Assert.True(mob.Statuses.ContainsKey(MonsterStatus.Stun));
    }

    private static (V113MobStatusTickHandler Handler, FieldInstance Field, Mob Mob, List<byte[]> Received) Setup()
    {
        var registry = new InMemoryMapSessionRegistry();
        var field = new FieldInstance(100000100);
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, 100, MaxMp: 0, Level: 1, Exp: 1),
            objectId: 100001);
        field.Add(mob);
        var received = new List<byte[]>();
        var player = new Player(new Character { Id = 1, Name = "Watcher" }, new Position(0, 0, 0, 0));
        registry.Register(field.MapId, 1, player, (pkt, _) => { received.Add(pkt); return Task.CompletedTask; }, new object());
        return (new V113MobStatusTickHandler(registry, new CombatService(new MapService(new EmptyDataProvider()))), field, mob, received);
    }
    [Fact]
    public async Task TickAsync_Poison_DealsDamageAndShowsHpToRecentAttacker()
    {
        var (handler, field, mob, received) = Setup();
        mob.RecordAttacker(1, DateTimeOffset.UtcNow);
        mob.TryApplyStatus(new MonsterStatusEffect(MonsterStatus.Poison, 30, 2101005, null, DateTimeOffset.UtcNow.AddMinutes(1), PoisonDamage: 30));

        await handler.TickAsync(field, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(70, mob.Hp);
        Assert.Equal(V113CombatPackets.ShowMonsterHp(mob.ObjectId, 70), Assert.Single(received));
    }

    [Fact]
    public async Task TickAsync_Poison_NeverKills()
    {
        var (handler, field, mob, _) = Setup();
        mob.TryApplyStatus(new MonsterStatusEffect(MonsterStatus.Poison, 500, 2101005, null, DateTimeOffset.UtcNow.AddMinutes(1), PoisonDamage: 500));

        await handler.TickAsync(field, DateTimeOffset.UtcNow, CancellationToken.None);
        await handler.TickAsync(field, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(1, mob.Hp);
        Assert.Same(mob, field.Get(mob.ObjectId));
    }

    private sealed class EmptyDataProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => throw new NotSupportedException();

        public IDataNode? GetAt(string fileName, string path) => null;
    }
}
