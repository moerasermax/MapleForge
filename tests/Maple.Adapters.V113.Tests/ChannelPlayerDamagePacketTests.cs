using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelPlayerDamagePacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x29, V113ChannelRecvOp.TakeDamage);
        Assert.Equal(unchecked((short)0xB8), V113ChannelSendOp.DamagePlayer);
    }

    [Fact]
    public void ParseTakeDamage_ReadsMonsterDamageLayout()
    {
        var body = new PacketWriter()
            .WriteInt(1234)
            .WriteByte(0)
            .WriteByte(1)
            .WriteInt(42)
            .WriteInt(100100)
            .WriteInt(200001)
            .WriteByte(1)
            .ToArray();

        var request = V113PlayerDamagePackets.ParseTakeDamage(new PacketReader(body));

        Assert.Equal(1234, request.Tick);
        Assert.Equal((sbyte)0, request.Type);
        Assert.Equal(42, request.Damage);
        Assert.Equal(100100, request.MonsterIdFrom);
        Assert.Equal(200001, request.ObjectId);
        Assert.Equal((byte)1, request.Direction);
    }

    [Fact]
    public void ParseTakeDamage_ReadsMapDamageLayout()
    {
        var body = new PacketWriter()
            .WriteInt(1234)
            .WriteByte(0xFE)
            .WriteByte(0)
            .WriteInt(10)
            .ToArray();

        var request = V113PlayerDamagePackets.ParseTakeDamage(new PacketReader(body));

        Assert.True(request.IsMapDamage);
        Assert.Equal((sbyte)-2, request.Type);
        Assert.Equal(10, request.Damage);
        Assert.Equal(0, request.ObjectId);
    }

    [Fact]
    public void DamagePlayer_WritesJavaNoReflectLayout()
    {
        var reader = new PacketReader(V113PlayerDamagePackets.DamagePlayer(
            characterId: 7,
            type: 0,
            damage: 42,
            monsterIdFrom: 100100,
            direction: 1));

        Assert.Equal(V113ChannelSendOp.DamagePlayer, reader.ReadShort());
        Assert.Equal(7, reader.ReadInt());
        Assert.Equal((byte)0, reader.ReadByte());
        Assert.Equal(42, reader.ReadInt());
        Assert.Equal(100100, reader.ReadInt());
        Assert.Equal((byte)1, reader.ReadByte());
        Assert.Equal((short)0, reader.ReadShort());
        Assert.Equal(42, reader.ReadInt());
        Assert.Equal(0, reader.Remaining);
    }
}
