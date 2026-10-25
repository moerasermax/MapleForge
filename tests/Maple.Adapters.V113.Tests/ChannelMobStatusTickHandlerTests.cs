using Maple.Adapters.V113.Channel;
using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.Skills;
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

    [Fact]
    public async Task TickAsync_ExpiredMist_RemovesAndBroadcastsRemoveMist()
    {
        var (handler, field, _, received) = Setup();
        var mist = new Mist(200001, 100100, 131, 1, true, 0, 0, (0, 0, 10, 10), T0.AddSeconds(1));
        field.Add(mist);

        await handler.TickAsync(field, T0.AddSeconds(1), CancellationToken.None);

        Assert.Null(field.Get(mist.ObjectId));
        Assert.Equal(V113MistPackets.RemoveMist(200001), Assert.Single(received));
    }

    [Fact]
    public void SpawnMist_MatchesJavaLayout()
    {
        var mist = new Mist(200001, 7, 2111003, 20, false, 1, 8, (-10, -20, 30, 40), T0);
        var r = new PacketReader(V113MistPackets.SpawnMist(mist));

        Assert.Equal(0x10C, r.ReadShort());
        Assert.Equal(200001, r.ReadInt());
        Assert.Equal(1, r.ReadInt());        // 玩家毒霧
        Assert.Equal(7, r.ReadInt());
        Assert.Equal(2111003, r.ReadInt());
        Assert.Equal(20, r.ReadByte());
        Assert.Equal(8, r.ReadShort());
        Assert.Equal(-10, r.ReadInt());
        Assert.Equal(-20, r.ReadInt());
        Assert.Equal(20, r.ReadInt());       // x + width
        Assert.Equal(20, r.ReadInt());       // y + height
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public async Task TickAsync_PlayerPoisonMist_BroadcastsApplyMonsterStatus()
    {
        var (handler, field, mob, received, player) = SetupWithMists();
        player.ChangeSkillLevel(2111003, 30, 30);
        var mists = new PlayerMistService();
        var effect = new MapleStatEffect { SourceId = 2111003, Level = 30, DurationMilliseconds = 40_000, Area = new MobSkillArea(-200, -150, 200, 150) };
        var mist = mists.TrySpawn(field, player, effect, new Position(0, 0, 0, 0), true, T0)!;

        await handler.TickAsync(field, T0.AddMilliseconds(2000), CancellationToken.None);

        var status = Assert.Contains(MonsterStatus.Poison, mob.Statuses);
        Assert.Equal(V113MonsterStatusCodec.ApplyMonsterStatus(mob.ObjectId, [status]), Assert.Single(received));
        Assert.Equal(100, mob.Hp); // 剛上的毒下一跳才扣血
        Assert.NotNull(field.Get(mist.ObjectId));
    }

    [Fact]
    public async Task TickAsync_RecoveryAura_SendsMpUpdateToRestoredPlayer()
    {
        var (handler, field, _, received, player) = SetupWithMists();
        player.Character.Stats.MaxMp = 1000;
        player.Character.Stats.Mp = 100;
        var effect = new MapleStatEffect { SourceId = 22161003, Level = 10, X = 10, DurationMilliseconds = 30_000, Area = new MobSkillArea(-200, -150, 200, 150) };
        new PlayerMistService().TrySpawn(field, player, effect, new Position(0, 0, 0, 0), true, T0);

        await handler.TickAsync(field, T0.AddMilliseconds(2000), CancellationToken.None);

        Assert.Equal((short)200, player.Mp);
        Assert.Equal(V113StatsPackets.UpdateStats([new PlayerStatUpdate(PlayerStatKind.Mp, 200)]), Assert.Single(received));
    }

    private static (V113MobStatusTickHandler Handler, FieldInstance Field, Mob Mob, List<byte[]> Received, Player Player) SetupWithMists()
    {
        var registry = new InMemoryMapSessionRegistry();
        var field = new FieldInstance(100000100);
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, 100, MaxMp: 0, Level: 1, Exp: 1),
            objectId: 100001);
        field.Add(mob);
        var received = new List<byte[]>();
        var player = new Player(new Character { Id = 1, Name = "FP", Job = 211 }, new Position(0, 0, 0, 0));
        field.Add(player);
        registry.Register(field.MapId, 1, player, (pkt, _) => { received.Add(pkt); return Task.CompletedTask; }, new object());
        var handler = new V113MobStatusTickHandler(
            registry,
            new CombatService(new MapService(new EmptyDataProvider())),
            new PlayerMistService());
        return (handler, field, mob, received, player);
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
