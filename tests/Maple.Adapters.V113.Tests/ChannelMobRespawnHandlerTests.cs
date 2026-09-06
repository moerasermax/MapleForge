using Maple.Adapters.V113.Channel;
using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Core.Data;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>
/// P067（M4-2 世界 tick 第四步）：<see cref="V113MobRespawnHandler.RespawnMonstersAsync"/>——單一
/// field 的怪物重生廣播（對照 Java <c>map.spawnMonster(monster, -2)</c>）。排程本身
/// （PeriodicTimer/多久跑一次）由 Maple.Host.Shared 的 WorldTickHostedService 負責，不在這裡測試
/// 範圍——這裡只驗證「給定一個 field + now，該廣播什麼給誰」。
/// </summary>
public sealed class ChannelMobRespawnHandlerTests
{
    [Fact]
    public async Task RespawnMonstersAsync_SpawnPointReady_BroadcastsSpawnMonsterToAllMapPlayers()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var combat = new CombatService(new MapService(new MonsterDataProvider()), timeProvider: new FakeTimeProvider(now));
        var mapRegistry = new InMemoryMapSessionRegistry();
        var handler = new V113MobRespawnHandler(combat, mapRegistry);
        var field = new FieldInstance(100000100);
        combat.SpawnMapMonsters(field, 100000100); // 先建立初始怪物 + 重生點（fixture mobTime=0/會走動，單點上限 2 隻）。

        var received = new List<(int CharId, byte[] Packet)>();
        var alice = NewPlayer(1, "Alice");
        var bob = NewPlayer(2, "Bob");
        mapRegistry.Register(field.MapId, alice.Character.Id, alice,
            (pkt, _) => { received.Add((1, pkt)); return Task.CompletedTask; }, new object());
        mapRegistry.Register(field.MapId, bob.Character.Id, bob,
            (pkt, _) => { received.Add((2, pkt)); return Task.CompletedTask; }, new object());

        await handler.RespawnMonstersAsync(field, now, CancellationToken.None);

        Assert.Equal(2, received.Count);
        var newMob = field.Objects.OfType<Mob>().Single(m => m.ObjectId != CombatService.DefaultMobObjectIdBase + 1);
        foreach (var (_, packet) in received)
        {
            var reader = new PacketReader(packet);
            Assert.Equal(0xE5, reader.ReadShort()); // SpawnMonsterOp
            Assert.Equal(newMob.ObjectId, reader.ReadInt());
        }
    }

    [Fact]
    public async Task RespawnMonstersAsync_NothingToSpawn_DoesNotBroadcast()
    {
        var combat = new CombatService(new MapService(new EmptyDataProvider()));
        var mapRegistry = new InMemoryMapSessionRegistry();
        var handler = new V113MobRespawnHandler(combat, mapRegistry);
        var field = new FieldInstance(100000100); // 沒有任何重生點。

        var received = new List<byte[]>();
        var alice = NewPlayer(1, "Alice");
        mapRegistry.Register(field.MapId, alice.Character.Id, alice,
            (pkt, _) => { received.Add(pkt); return Task.CompletedTask; }, new object());

        await handler.RespawnMonstersAsync(field, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Empty(received);
    }

    private static Player NewPlayer(int id, string name) =>
        new(new Core.Characters.Character { Id = id, Name = name }, new Position(0, 0, 0, 0));

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class EmptyDataProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path) => null;
    }

    private sealed class MonsterDataProvider : IDataProvider
    {
        private readonly Dictionary<string, IDataNode> _nodes;

        public MonsterDataProvider()
        {
            var map = new Node("100000100.img", children: new Dictionary<string, IDataNode>
            {
                ["info"] = new Node("info", children: new Dictionary<string, IDataNode>
                {
                    ["returnMap"] = new Node("returnMap", 100000000),
                    ["town"] = new Node("town", 0),
                }),
                ["portal"] = new Node("portal"),
                ["foothold"] = new Node("foothold"),
                ["life"] = new Node("life", children: new Dictionary<string, IDataNode>
                {
                    ["0"] = new Node("0", children: new Dictionary<string, IDataNode>
                    {
                        ["type"] = new Node("type", "m"),
                        ["id"] = new Node("id", "100100"),
                        ["x"] = new Node("x", 30),
                        ["y"] = new Node("y", 40),
                        ["cy"] = new Node("cy", 45),
                        ["f"] = new Node("f", 0),
                        ["fh"] = new Node("fh", 7),
                        ["rx0"] = new Node("rx0", 20),
                        ["rx1"] = new Node("rx1", 60),
                        ["mobTime"] = new Node("mobTime", 0),
                        ["team"] = new Node("team", -1),
                    }),
                }),
            });

            var mob = new Node("0100100.img", children: new Dictionary<string, IDataNode>
            {
                ["info"] = new Node("info", children: new Dictionary<string, IDataNode>
                {
                    ["maxHP"] = new Node("maxHP", 42),
                    ["maxMP"] = new Node("maxMP", 7),
                    ["level"] = new Node("level", 2),
                    ["exp"] = new Node("exp", 12),
                }),
                ["move"] = new Node("move"),
            });

            _nodes = new Dictionary<string, IDataNode>
            {
                ["Map|Map/Map1/100000100.img"] = map,
                ["Mob|0100100.img"] = mob,
            };
        }

        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path)
            => _nodes.TryGetValue($"{fileName}|{path}", out var node) ? node : null;
    }

    private sealed class Node : IDataNode
    {
        public Node(string name, object? value = null, IReadOnlyDictionary<string, IDataNode>? children = null)
        {
            Name = name;
            Value = value;
            Children = children ?? new Dictionary<string, IDataNode>();
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, IDataNode> Children { get; }

        public object? Value { get; }

        public IDataNode? this[string name] => Children.TryGetValue(name, out var child) ? child : null;
    }
}
