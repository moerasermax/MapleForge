using Maple.Adapters.V113.Channel;
using Maple.Application.Stats;
using Maple.Core.Accounts;
using Maple.Core.CashShop;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelBuffItemPacketTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OpcodeConstants_MatchJavaRecvAndSendValues()
    {
        Assert.Equal(unchecked((short)0x9B), V113BuffItemPackets.RecvSolomon);
        Assert.Equal(unchecked((short)0x9C), V113BuffItemPackets.RecvGachExp);
        Assert.Equal(unchecked((short)0xA0), V113BuffItemPackets.RecvTransformPlayer);
        Assert.Equal(unchecked((short)0xA2), V113BuffItemPackets.RecvXmasSurprise);
        Assert.Equal(0x161, V113BuffItemPackets.SendXmasSurprise);
    }

    [Fact]
    public void ParseSolomon_ReadsJavaFields()
    {
        var body = new PacketWriter()
            .WriteInt(123)
            .WriteShort(2)
            .WriteInt(2370005)
            .ToArray();

        var request = V113BuffItemPackets.ParseSolomon(new PacketReader(body));

        Assert.Equal(123, request.Tick);
        Assert.Equal((short)2, request.Slot);
        Assert.Equal(2370005, request.ItemId);
    }

    [Fact]
    public void HandleSolomon_GivesPendingExpAndInventoryUpdate()
    {
        var player = MakePlayer("Solomon", level: 20);
        player.Inventory.By(InventoryType.Use).Put(new Item { ItemId = 2370005, Slot = 2, Quantity = 1 });
        var handler = NewHandler();

        var result = handler.HandleSolomon(new PacketReader(SolomonBody(2, 2370005)), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Equal(3, result.Packets.Count);
        Assert.Equal(5_000, player.Character.GachExp);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(2));
        Assert.Equal(V113StatsPackets.SendUpdateStats, BitConverter.ToInt16(result.Packets[0], 0));
        Assert.Equal(V113BuffItemPackets.SendModifyInventoryItem, BitConverter.ToInt16(result.Packets[1], 0));
        Assert.Equal(V113StatsPackets.SendUpdateStats, BitConverter.ToInt16(result.Packets[2], 0));
    }

    [Fact]
    public void HandleGachExp_GrantsExperienceAndClearsPendingExp()
    {
        var player = MakePlayer("Gach", level: 20);
        player.Character.GachExp = 500;
        var handler = NewHandler(statsService: new StatsService(TimeProvider.System, static (_, _) => 1));

        var result = handler.HandleGachExp(new PacketReader(new PacketWriter().WriteInt(123).ToArray()), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Equal(0, player.Character.GachExp);
        Assert.Equal(500, player.Character.Exp);
        Assert.Equal(4, result.Packets.Count);
        Assert.Equal(V113BuffItemPackets.SendShowStatusInfo, BitConverter.ToInt16(result.Packets[2], 0));
    }

    [Fact]
    public void HandleTransformPlayer_AppliesMorphBuffAndReturnsBroadcast()
    {
        var source = MakePlayer("Caster", level: 20);
        var target = MakePlayer("Target", level: 20, id: 2);
        source.Inventory.By(InventoryType.Use).Put(new Item { ItemId = 2212000, Slot = 1, Quantity = 1 });
        var handler = NewHandler(new FakeTimeProvider(FixedNow));

        var result = handler.HandleTransformPlayer(
            new PacketReader(TransformBody(1, 2212000, "target")),
            source,
            new[] { source, target });

        Assert.True(result.Handled);
        Assert.True(result.SourceCharacterMutated);
        Assert.True(result.TargetRuntimeMutated);
        Assert.Same(target, result.Target);
        Assert.Null(source.Inventory.By(InventoryType.Use).Get(1));
        var buff = Assert.Single(target.ActiveBuffs);
        Assert.Equal(MapleBuffStat.MORPH, buff.Stat);
        Assert.Equal(23, buff.Value);
        Assert.Equal(V113SkillPackets.GiveBuffOp, BitConverter.ToInt16(Assert.Single(result.TargetPackets), 0));
        var foreign = Assert.Single(result.BroadcastPackets);
        Assert.Equal(V113BuffItemPackets.SendGiveForeignBuff, BitConverter.ToInt16(foreign, 0));
        Assert.Equal(target.Character.Id, BitConverter.ToInt32(foreign, 2));
    }

    [Fact]
    public void ShowXmasSurprise_WritesJavaSuccessLayout()
    {
        var item = new Item
        {
            ItemId = 5350000,
            Quantity = 1,
            UniqueId = 99,
            Expiration = -1,
        };

        var packet = V113BuffItemPackets.ShowXmasSurprise(false, 77, item, accountId: 7);

        Assert.Equal(78, packet.Length);
        Assert.Equal(V113BuffItemPackets.SendXmasSurprise, BitConverter.ToInt16(packet, 0));
        Assert.Equal(223, packet[2]);
        Assert.Equal(77, BitConverter.ToInt64(packet, 3));
        Assert.Equal(99, BitConverter.ToInt64(packet, 15));
        Assert.Equal(7, BitConverter.ToInt64(packet, 23));
        Assert.Equal(5350000, BitConverter.ToInt32(packet, 31));
        Assert.Equal(5350000, BitConverter.ToInt32(packet, 72));
        Assert.Equal(1, packet[76]);
        Assert.Equal(1, packet[77]);
    }

    [Fact]
    public void HandleXmasSurprise_ConsumesBoxAndReturnsRewardPacket()
    {
        var player = MakePlayer("Xmas", level: 20);
        player.Inventory.By(InventoryType.Cash).Put(new Item
        {
            ItemId = 5222000,
            Slot = 1,
            Quantity = 1,
            UniqueId = 77,
        });
        var account = new Account { Id = 7 };
        var reward = new CashItemDefinition(20300223, 5350000, 1, 0, 45, 2, -1, true);
        var handler = NewHandler(new FakeTimeProvider(FixedNow), new FakeCashItemCatalog(reward), new FixedRewardSource(20300223));

        var result = handler.HandleXmasSurprise(
            new PacketReader(new PacketWriter().WriteLong(77).ToArray()),
            account,
            player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Null(player.Inventory.By(InventoryType.Cash).Get(1));
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).CountById(5350000));
        var packet = Assert.Single(result.Packets);
        Assert.Equal(V113BuffItemPackets.SendXmasSurprise, BitConverter.ToInt16(packet, 0));
        Assert.Equal(223, packet[2]);
    }

    private static V113BuffItemHandler NewHandler(
        TimeProvider? timeProvider = null,
        ICashItemCatalog? catalog = null,
        IV113XmasSurpriseRewardSource? rewardSource = null,
        StatsService? statsService = null)
        => new(
            statsService ?? new StatsService(TimeProvider.System, static (_, _) => 1),
            catalog ?? new FakeCashItemCatalog(),
            rewardSource ?? new FixedRewardSource(20300223),
            timeProvider ?? new FakeTimeProvider(FixedNow));

    private static byte[] SolomonBody(short slot, int itemId)
        => new PacketWriter()
            .WriteInt(123)
            .WriteShort(slot)
            .WriteInt(itemId)
            .ToArray();

    private static byte[] TransformBody(short slot, int itemId, string targetName)
        => new PacketWriter()
            .WriteInt(123)
            .WriteShort(slot)
            .WriteInt(itemId)
            .WriteMapleString(targetName)
            .ToArray();

    private static Player MakePlayer(string name, byte level, int id = 1)
        => new(
            new Character
            {
                Id = id,
                Name = name,
                Level = level,
                Stats = new CharacterStats { Hp = 50, MaxHp = 50, Mp = 50, MaxMp = 50 },
            },
            new Position(0, 0, 0, 0));

    private sealed class FakeCashItemCatalog : ICashItemCatalog
    {
        private readonly Dictionary<int, CashItemDefinition> _items;

        public FakeCashItemCatalog(params CashItemDefinition[] items)
        {
            _items = items.ToDictionary(static i => i.SerialNumber);
        }

        public CashItemDefinition? GetBySerialNumber(int serialNumber)
            => _items.GetValueOrDefault(serialNumber);
    }

    private sealed class FixedRewardSource : IV113XmasSurpriseRewardSource
    {
        private readonly int _serialNumber;

        public FixedRewardSource(int serialNumber)
        {
            _serialNumber = serialNumber;
        }

        public int NextSerialNumber() => _serialNumber;
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
