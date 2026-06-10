using Maple.Adapters.V113.Channel;
using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelSkillMacroPacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x68, V113ChannelRecvOp.SkillMacro);
        Assert.Equal(0x7A, V113ChannelSendOp.SkillMacro);
    }

    [Fact]
    public void ParseChangeSkillMacro_ReadsJavaLayoutAndAssignsLoopIndexPosition()
    {
        var body = new PacketWriter()
            .WriteByte(2)
            .WriteMapleString("boss").WriteByte(1).WriteInt(100).WriteInt(101).WriteInt(102)
            .WriteMapleString("mob").WriteByte(0).WriteInt(200).WriteInt(201).WriteInt(202)
            .ToArray();

        var changes = V113SkillMacroPackets.ParseChangeSkillMacro(new PacketReader(body));

        Assert.Equal(2, changes.Count);
        Assert.Equal(new V113SkillMacroChange(0, "boss", 1, 100, 101, 102), changes[0]);
        Assert.Equal(new V113SkillMacroChange(1, "mob", 0, 200, 201, 202), changes[1]);
    }

    [Fact]
    public void ParseChangeSkillMacro_RejectsMoreThanFiveMacros()
    {
        var body = new PacketWriter().WriteByte(6).ToArray();

        Assert.Throws<InvalidDataException>(() =>
            V113SkillMacroPackets.ParseChangeSkillMacro(new PacketReader(body)));
    }

    [Fact]
    public void SkillMacros_EmptyCharacter_ReturnsNullLikeJavaSendMacros()
    {
        Assert.Null(V113SkillMacroPackets.SkillMacros(new Character()));
    }

    [Fact]
    public void SkillMacros_WritesOnlyExistingMacrosInJavaLayout()
    {
        var character = new Character();
        character.UpdateSkillMacro(1, "mob", shout: 0, skill1: 200, skill2: 201, skill3: 202);
        character.UpdateSkillMacro(0, "boss", shout: 1, skill1: 100, skill2: 101, skill3: 102);

        var packet = V113SkillMacroPackets.SkillMacros(character);
        Assert.NotNull(packet);

        var reader = new PacketReader(packet);
        Assert.Equal(V113ChannelSendOp.SkillMacro, reader.ReadShort());
        Assert.Equal((byte)2, reader.ReadByte());

        Assert.Equal("boss", reader.ReadMapleString());
        Assert.Equal((byte)1, reader.ReadByte());
        Assert.Equal(100, reader.ReadInt());
        Assert.Equal(101, reader.ReadInt());
        Assert.Equal(102, reader.ReadInt());

        Assert.Equal("mob", reader.ReadMapleString());
        Assert.Equal((byte)0, reader.ReadByte());
        Assert.Equal(200, reader.ReadInt());
        Assert.Equal(201, reader.ReadInt());
        Assert.Equal(202, reader.ReadInt());

        Assert.Equal(0, reader.Remaining);
    }
}
