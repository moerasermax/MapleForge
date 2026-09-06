using Maple.Adapters.V113.Channel;
using Maple.Application.Drops;
using Maple.Application.Maps;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>
/// P063（M4-2 世界 tick 第三步）：<see cref="V113DropExpiryHandler.ExpireDropsAsync"/>——單一 field
/// 的過期掉落物廣播（對照 Java <c>MapleMapItem.expire</c> 的 <c>removeItemFromMap(oid, 0, 0)</c>）。
/// 排程本身（PeriodicTimer/多久跑一次）由 Maple.Host.Shared 的 WorldTickHostedService 負責，不在
/// 這裡測試範圍——這裡只驗證「給定一個 field + now，該廣播什麼給誰」。
/// </summary>
public sealed class ChannelDropExpiryHandlerTests
{
    [Fact]
    public async Task ExpireDropsAsync_ExpiredDrop_BroadcastsRemoveItemFromMapToAllMapPlayers()
    {
        var drops = new DropService(new InMemoryMonsterDropCatalog(new Dictionary<int, IReadOnlyList<MonsterDropEntry>>()));
        var mapRegistry = new InMemoryMapSessionRegistry();
        var handler = new V113DropExpiryHandler(drops, mapRegistry);
        var field = new FieldInstance(100000000);
        var spawnedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var drop = MapDrop.ForItem(
            1_000_000,
            new Item { ItemId = 4000000, Quantity = 1 },
            new Position(0, 0, 0, 0),
            new Position(0, 0, 0, 0),
            sourceObjectId: 1,
            ownerId: 1,
            dropType: 0,
            spawnedAt: spawnedAt);
        field.Add(drop);

        var received = new List<(int CharId, byte[] Packet)>();
        var alice = NewPlayer(1, "Alice");
        var bob = NewPlayer(2, "Bob");
        mapRegistry.Register(field.MapId, alice.Character.Id, alice,
            (pkt, _) => { received.Add((1, pkt)); return Task.CompletedTask; }, new object());
        mapRegistry.Register(field.MapId, bob.Character.Id, bob,
            (pkt, _) => { received.Add((2, pkt)); return Task.CompletedTask; }, new object());

        await handler.ExpireDropsAsync(field, spawnedAt + MapDrop.ExpireAfter, CancellationToken.None);

        Assert.Null(field.Get(drop.ObjectId));
        Assert.Equal(2, received.Count);
        foreach (var (_, packet) in received)
        {
            var reader = new PacketReader(packet);
            Assert.Equal(unchecked((short)0x108), reader.ReadShort()); // SendRemoveItemFromMap
            Assert.Equal(0, reader.ReadByte());                        // animation=0（Expire）
            Assert.Equal(drop.ObjectId, reader.ReadInt());
            Assert.Equal(0, reader.Remaining);
        }
    }

    [Fact]
    public async Task ExpireDropsAsync_NothingExpired_DoesNotBroadcast()
    {
        var drops = new DropService(new InMemoryMonsterDropCatalog(new Dictionary<int, IReadOnlyList<MonsterDropEntry>>()));
        var mapRegistry = new InMemoryMapSessionRegistry();
        var handler = new V113DropExpiryHandler(drops, mapRegistry);
        var field = new FieldInstance(100000000);
        var spawnedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        field.Add(MapDrop.ForItem(
            1_000_000,
            new Item { ItemId = 4000000, Quantity = 1 },
            new Position(0, 0, 0, 0),
            new Position(0, 0, 0, 0),
            sourceObjectId: 1,
            ownerId: 1,
            dropType: 0,
            spawnedAt: spawnedAt));

        var received = new List<byte[]>();
        var alice = NewPlayer(1, "Alice");
        mapRegistry.Register(field.MapId, alice.Character.Id, alice,
            (pkt, _) => { received.Add(pkt); return Task.CompletedTask; }, new object());

        await handler.ExpireDropsAsync(field, spawnedAt + MapDrop.ExpireAfter - TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Empty(received);
        Assert.NotNull(field.Get(1_000_000));
    }

    private static Player NewPlayer(int id, string name) =>
        new(new Core.Characters.Character { Id = id, Name = name }, new Position(0, 0, 0, 0));
}
