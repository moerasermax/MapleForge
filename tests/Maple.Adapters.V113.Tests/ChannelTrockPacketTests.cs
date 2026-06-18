using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelTrockPacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x60, V113ChannelRecvOp.TrockAddMap);
        Assert.Equal(0x27, V113ChannelSendOp.MapTransferResult);
    }

    [Fact]
    public void ParseAddMap_ReadsDeleteTargetMap()
    {
        var body = new PacketWriter()
            .WriteByte(0)
            .WriteByte(1)
            .WriteInt(100000000)
            .ToArray();

        var request = V113TrockPackets.ParseAddMap(new PacketReader(body));

        Assert.True(request.IsDelete);
        Assert.True(request.IsVip);
        Assert.Equal(100000000, request.MapId);
    }

    [Fact]
    public void ParseAddMap_AddBranchDoesNotReadTargetMap()
    {
        var body = new PacketWriter()
            .WriteByte(1)
            .WriteByte(0)
            .ToArray();

        var request = V113TrockPackets.ParseAddMap(new PacketReader(body));

        Assert.True(request.IsAdd);
        Assert.False(request.IsVip);
        Assert.Equal(0, request.MapId);
    }

    [Fact]
    public void MapTransferResult_WritesRegularOrVipRefresh()
    {
        var character = new Character
        {
            RegularRocks =
            [
                100000000,
                Character.EmptyRockMapId,
                Character.EmptyRockMapId,
                Character.EmptyRockMapId,
                Character.EmptyRockMapId,
            ],
            VipRocks =
            [
                200000000,
                201000000,
                Character.EmptyRockMapId,
                Character.EmptyRockMapId,
                Character.EmptyRockMapId,
                Character.EmptyRockMapId,
                Character.EmptyRockMapId,
                Character.EmptyRockMapId,
                Character.EmptyRockMapId,
                Character.EmptyRockMapId,
            ],
        };

        var regular = new PacketReader(V113TrockPackets.MapTransferResult(character, vip: 0, delete: false));
        Assert.Equal(V113ChannelSendOp.MapTransferResult, regular.ReadShort());
        Assert.Equal((byte)3, regular.ReadByte());
        Assert.Equal((byte)0, regular.ReadByte());
        Assert.Equal(100000000, regular.ReadInt());
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(Character.EmptyRockMapId, regular.ReadInt());
        }
        Assert.Equal(0, regular.Remaining);

        var vip = new PacketReader(V113TrockPackets.MapTransferResult(character, vip: 1, delete: true));
        Assert.Equal(V113ChannelSendOp.MapTransferResult, vip.ReadShort());
        Assert.Equal((byte)2, vip.ReadByte());
        Assert.Equal((byte)1, vip.ReadByte());
        Assert.Equal(200000000, vip.ReadInt());
        Assert.Equal(201000000, vip.ReadInt());
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(Character.EmptyRockMapId, vip.ReadInt());
        }
        Assert.Equal(0, vip.Remaining);
    }

    [Fact]
    public void UseTeleRock_MapModeWithExistingMapReturnsWarpIntent()
    {
        var body = new PacketWriter()
            .WriteByte(0)
            .WriteByte(0)
            .WriteInt(100000000)
            .ToArray();
        var service = new MapService(new FakeDataProvider("Map/Map1/100000000.img"));

        var result = V113TeleRockHandler.HandleUseTeleRock(new PacketReader(body), service);

        Assert.True(result.Success);
        Assert.Equal(100000000, result.WarpMapId);
        Assert.Single(result.Packets);

        var packet = new PacketReader(result.Packets[0]);
        Assert.Equal(V113ChannelSendOp.MapTransferResult, packet.ReadShort());
        Assert.Equal((byte)0, packet.ReadByte());
        Assert.Equal(0, packet.Remaining);
    }

    [Fact]
    public void UseTeleRock_InvalidMapReturnsFailureAndEnableActions()
    {
        var body = new PacketWriter()
            .WriteByte(0)
            .WriteByte(0)
            .WriteInt(999999999)
            .ToArray();
        var service = new MapService(new FakeDataProvider());

        var result = V113TeleRockHandler.HandleUseTeleRock(new PacketReader(body), service);

        Assert.False(result.Success);
        Assert.Null(result.WarpMapId);
        Assert.Equal(2, result.Packets.Count);

        var packet = new PacketReader(result.Packets[0]);
        Assert.Equal(V113ChannelSendOp.MapTransferResult, packet.ReadShort());
        Assert.Equal((byte)1, packet.ReadByte());
        Assert.Equal(V113StatsPackets.EnableActions(), result.Packets[1]);
    }

    private sealed class FakeDataProvider : IDataProvider
    {
        private readonly HashSet<string> _paths;

        public FakeDataProvider(params string[] existingPaths)
        {
            _paths = existingPaths.ToHashSet(StringComparer.Ordinal);
        }

        public IDataNode GetRoot(string fileName) => new FakeDataNode(fileName);

        public IDataNode? GetAt(string fileName, string path)
            => fileName == "Map" && _paths.Contains(path) ? new FakeDataNode(path) : null;
    }

    private sealed class FakeDataNode : IDataNode
    {
        public FakeDataNode(string name) => Name = name;

        public string Name { get; }

        public IReadOnlyDictionary<string, IDataNode> Children { get; } =
            new Dictionary<string, IDataNode>();

        public object? Value => null;

        public IDataNode? this[string name] => null;
    }
}
