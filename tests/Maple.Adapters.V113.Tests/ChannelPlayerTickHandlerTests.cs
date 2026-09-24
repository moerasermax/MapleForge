using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>
/// P073（M4-2 世界 tick 逐玩家處理）：<see cref="V113PlayerTickHandler.TickPlayersAsync"/>——對照 Java
/// <c>World.handleCooldowns</c>，給定 field + now，驗證對場上每個玩家送了什麼。
/// </summary>
public sealed class ChannelPlayerTickHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TickPlayersAsync_ExpiredCooldown_RemovesAndSendsZeroCooldownToOwnerOnly()
    {
        var (handler, registry, field) = Build();
        var alice = NewPlayer(1, "Alice");
        var bob = NewPlayer(2, "Bob");
        alice.AddSkillCooldown(1121010, Now.AddSeconds(-40), seconds: 30); // 已到期
        alice.AddSkillCooldown(1121008, Now.AddSeconds(-5), seconds: 30);  // 尚未到期
        var received = Register(registry, field, alice, bob);

        await handler.TickPlayersAsync(field, Now, CancellationToken.None);

        var (charId, packet) = Assert.Single(received);
        Assert.Equal(1, charId);
        Assert.Equal(V113SkillPackets.SkillCooldown(1121010, 0), packet);
        Assert.False(alice.SkillIsCooling(1121010, Now));
        Assert.True(alice.SkillIsCooling(1121008, Now));
    }

    [Fact]
    public async Task TickPlayersAsync_SecondTick_DoesNotResendAlreadyExpiredCooldown()
    {
        var (handler, registry, field) = Build();
        var alice = NewPlayer(1, "Alice");
        alice.AddSkillCooldown(1121010, Now.AddSeconds(-40), seconds: 30);
        var received = Register(registry, field, alice);

        await handler.TickPlayersAsync(field, Now, CancellationToken.None);
        await handler.TickPlayersAsync(field, Now.AddSeconds(3), CancellationToken.None);

        Assert.Single(received);
    }

    [Fact]
    public async Task TickPlayersAsync_NoCooldowns_SendsNothing()
    {
        var (handler, registry, field) = Build();
        var received = Register(registry, field, NewPlayer(1, "Alice"));

        await handler.TickPlayersAsync(field, Now, CancellationToken.None);

        Assert.Empty(received);
    }

    [Fact]
    public async Task TickPlayersAsync_ColdMap_SendsHpUpdateAndEnableActionsOnDeath()
    {
        var (handler, registry, field) = Build();
        field.HpDecay = Core.World.FieldHpDecay.Create(
            new Core.Maps.MapData { MapId = field.MapId, DecHp = 20, DecHpInterval = 10_000 },
            Now.AddSeconds(-11));
        var healthy = NewPlayer(1, "Alice", hp: 100);
        var dying = NewPlayer(2, "Bob", hp: 10);
        var received = Register(registry, field, healthy, dying);

        await handler.TickPlayersAsync(field, Now, CancellationToken.None);

        var hpUpdate80 = V113StatsPackets.UpdateStats(new[] { new PlayerStatUpdate(PlayerStatKind.Hp, 80) });
        var hpUpdate0 = V113StatsPackets.UpdateStats(new[] { new PlayerStatUpdate(PlayerStatKind.Hp, 0) });
        Assert.Equal(new[] { hpUpdate80 }, received.Where(r => r.CharId == 1).Select(r => r.Packet));
        Assert.Equal(new[] { V113StatsPackets.EnableActions(), hpUpdate0 }, received.Where(r => r.CharId == 2).Select(r => r.Packet));
    }

    private static (V113PlayerTickHandler Handler, InMemoryMapSessionRegistry Registry, FieldInstance Field) Build()
    {
        var registry = new InMemoryMapSessionRegistry();
        var handler = new V113PlayerTickHandler(
            new SkillService(new InMemorySkillCatalog(Array.Empty<Core.Skills.MapleSkill>())),
            new FieldHazardService(),
            registry);
        return (handler, registry, new FieldInstance(100000000));
    }

    private static List<(int CharId, byte[] Packet)> Register(InMemoryMapSessionRegistry registry, FieldInstance field, params Player[] players)
    {
        var received = new List<(int, byte[])>();
        foreach (var p in players)
        {
            var id = p.Character.Id;
            registry.Register(field.MapId, id, p, (pkt, _) => { received.Add((id, pkt)); return Task.CompletedTask; }, new object());
        }

        return received;
    }

    private static Player NewPlayer(int id, string name, short hp = 50) =>
        new(new Core.Characters.Character
        {
            Id = id,
            Name = name,
            Stats = new Core.Characters.CharacterStats { Hp = hp, MaxHp = 100 },
        }, new Position(0, 0, 0, 0));
}
