using Maple.Adapters.V113.Channel;
using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelCharacterInfoPacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x5B, V113ChannelRecvOp.CharInfoRequest);
        Assert.Equal(0x97, V113ChannelRecvOp.UpdateCharInfo);
        Assert.Equal(0x36, V113ChannelSendOp.CharInfo);
    }

    [Fact]
    public void ParseCharInfoRequest_ReadsTargetCharacterId()
    {
        var body = new PacketWriter().WriteInt(1234).ToArray();

        Assert.Equal(1234, V113CharacterInfoPackets.ParseCharInfoRequest(new PacketReader(body)));
    }

    [Fact]
    public void ParseUpdateCharInfo_ReadsMessageExpressionAndBirthdayBranches()
    {
        var message = new PacketWriter()
            .WriteByte(0)
            .WriteMapleString("hello")
            .ToArray();
        var expression = new PacketWriter()
            .WriteByte(1)
            .WriteByte(7)
            .ToArray();
        var birthday = new PacketWriter()
            .WriteByte(2)
            .WriteByte(1)
            .WriteByte(6)
            .WriteByte(10)
            .WriteByte(9)
            .ToArray();

        var msg = V113CharacterInfoPackets.ParseUpdateCharInfo(new PacketReader(message));
        var exp = V113CharacterInfoPackets.ParseUpdateCharInfo(new PacketReader(expression));
        var birth = V113CharacterInfoPackets.ParseUpdateCharInfo(new PacketReader(birthday));

        Assert.Equal(V113CharacterInfoUpdateKind.CharacterMessage, msg.Kind);
        Assert.Equal("hello", msg.Message);
        Assert.Equal(V113CharacterInfoUpdateKind.Expression, exp.Kind);
        Assert.Equal((byte)7, exp.Expression);
        Assert.Equal(V113CharacterInfoUpdateKind.Birthday, birth.Kind);
        Assert.Equal((byte)1, birth.Blood);
        Assert.Equal((byte)6, birth.BirthMonth);
        Assert.Equal((byte)10, birth.BirthDay);
        Assert.Equal((byte)9, birth.Constellation);
    }

    [Fact]
    public void ParseUpdateCharInfo_EmptyPacketRequestsEnableActions()
    {
        var request = V113CharacterInfoPackets.ParseUpdateCharInfo(new PacketReader([]));

        Assert.Equal(V113CharacterInfoUpdateKind.None, request.Kind);
    }

    [Fact]
    public void CharInfo_WritesBasicJavaLayoutWithProfileFields()
    {
        var character = new Character
        {
            Id = 1234,
            Name = "Target",
            Level = 45,
            Job = 110,
            Fame = 7,
            CharacterMessage = "ready",
            ProfileExpression = 4,
            Constellation = 9,
            Blood = 1,
            BirthMonth = 6,
            BirthDay = 10,
        };

        var packet = V113CharacterInfoPackets.CharInfo(
            character,
            new V113CharacterInfoSocial("Guild", "Alliance"));
        var reader = new PacketReader(packet);

        Assert.Equal(V113ChannelSendOp.CharInfo, reader.ReadShort());
        Assert.Equal(1234, reader.ReadInt());
        Assert.Equal((byte)45, reader.ReadByte());
        Assert.Equal((short)110, reader.ReadShort());
        Assert.Equal((short)7, reader.ReadShort());
        Assert.Equal((byte)0, reader.ReadByte());
        Assert.Equal("Guild", reader.ReadMapleString());
        Assert.Equal("Alliance", reader.ReadMapleString());
        Assert.Equal("ready", reader.ReadMapleString());
        Assert.Equal((byte)4, reader.ReadByte()); // expression
        Assert.Equal((byte)9, reader.ReadByte()); // constellation
        Assert.Equal((byte)1, reader.ReadByte()); // blood
        Assert.Equal((byte)6, reader.ReadByte()); // month
        Assert.Equal((byte)10, reader.ReadByte()); // day
        Assert.Equal((byte)0, reader.ReadByte()); // pet terminator
        Assert.Equal((byte)0, reader.ReadByte()); // mount
        Assert.Equal((byte)0, reader.ReadByte()); // wishlist
        Assert.Equal(1, reader.ReadInt()); // monster book level
        Assert.Equal(0, reader.ReadInt()); // normal cards
        Assert.Equal(0, reader.ReadInt()); // special cards
        Assert.Equal(0, reader.ReadInt()); // total cards
        Assert.Equal(0, reader.ReadInt()); // cover mob id
        Assert.Equal(0, reader.ReadInt()); // medal
        Assert.Equal((short)0, reader.ReadShort());
        Assert.Equal(0, reader.Remaining);
    }
}
