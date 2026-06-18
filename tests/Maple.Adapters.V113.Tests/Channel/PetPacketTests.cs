using Maple.Adapters.V113.Channel;
using Maple.Core.IO;
using Maple.Core.Pets;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests.Channel;

public sealed class PetPacketTests
{
    [Fact]
    public void OpcodeConstants_MatchJavaProperties()
    {
        Assert.Equal(0x46, V113PetPackets.RecvPetFood);
        Assert.Equal(0x5C, V113PetPackets.RecvSpawnPet);
        Assert.Equal(unchecked((short)0xA4), V113PetPackets.RecvMovePet);
        Assert.Equal(unchecked((short)0xA5), V113PetPackets.RecvPetChat);
        Assert.Equal(unchecked((short)0xA6), V113PetPackets.RecvPetCommand);
        Assert.Equal(unchecked((short)0xA7), V113PetPackets.RecvPetLoot);
        Assert.Equal(unchecked((short)0xA8), V113PetPackets.RecvPetAutoPot);
        Assert.Equal(unchecked((short)0xA9), V113PetPackets.RecvPetIgnore);

        Assert.Equal(unchecked((short)0xA2), V113PetPackets.SendSpawnPet);
        Assert.Equal(unchecked((short)0xA5), V113PetPackets.SendMovePet);
        Assert.Equal(unchecked((short)0xA6), V113PetPackets.SendPetChat);
        Assert.Equal(unchecked((short)0xA7), V113PetPackets.SendPetNameChange);
        Assert.Equal(unchecked((short)0xA9), V113PetPackets.SendPetCommand);
        Assert.Equal(unchecked((short)0xCE), V113PetPackets.SendPetFlagChange);
    }

    [Fact]
    public void ParseSpawnPet_ReadsTickSlotAndLeadFlag()
    {
        var w = new PacketWriter();
        w.WriteInt(123);
        w.WriteShort(7);
        w.WriteByte(1);

        var request = V113PetPackets.ParseSpawnPet(new PacketReader(w.ToArray()));

        Assert.Equal(123, request.Tick);
        Assert.Equal(7, request.CashSlot);
        Assert.True(request.Lead);
    }

    [Fact]
    public void ParsePetChat_ReadsLongPetIdCommandAndAsciiText()
    {
        var w = new PacketWriter();
        w.WriteLong(1001);
        w.WriteShort(2);
        w.WriteShort(5);
        w.WriteBytes("hello"u8);

        var request = V113PetPackets.ParsePetChat(new PacketReader(w.ToArray()));

        Assert.Equal(1001, request.PetId);
        Assert.Equal(2, request.Command);
        Assert.Equal("hello", request.Text);
    }

    [Fact]
    public void SpawnPet_WritesOpcodeCharacterSlotAndPetBody()
    {
        var packet = V113PetPackets.SpawnPet(123, 0, CreatePet());

        Assert.Equal(V113PetPackets.SendSpawnPet, BitConverter.ToInt16(packet, 0));
        Assert.Equal(123, BitConverter.ToInt32(packet, 2));
        Assert.Equal(0, packet[6]);
        Assert.Equal(1, packet[7]);
        Assert.Equal(0, packet[8]);
        Assert.Equal(5000000, BitConverter.ToInt32(packet, 9));
        Assert.Contains((byte)'K', packet);
    }

    [Fact]
    public void MovePet_WritesOpcodeAndAppendsRawMovement()
    {
        byte[] rawMovement = [0x01, 0x02, 0x03, 0x04];

        var packet = V113PetPackets.MovePet(123, 0, 1001, rawMovement);

        Assert.Equal(V113PetPackets.SendMovePet, BitConverter.ToInt16(packet, 0));
        Assert.Equal(123, BitConverter.ToInt32(packet, 2));
        Assert.Equal(0, packet[6]);
        Assert.Equal(1001, BitConverter.ToInt32(packet, 7));
        Assert.Equal(rawMovement, packet[^rawMovement.Length..]);
    }

    [Fact]
    public void PetChat_WritesOpcodeCommandQuoteFlagAndText()
    {
        var packet = V113PetPackets.PetChat(123, 0, 4, "hi");

        Assert.Equal(V113PetPackets.SendPetChat, BitConverter.ToInt16(packet, 0));
        Assert.Equal(123, BitConverter.ToInt32(packet, 2));
        Assert.Equal(0, packet[6]);
        Assert.Equal(4, packet[7]);
        Assert.Equal(0, packet[8]);
        Assert.Equal(2, BitConverter.ToInt16(packet, 9));
        Assert.Equal((byte)'h', packet[11]);
        Assert.Equal((byte)'i', packet[12]);
    }

    [Fact]
    public void PetCommand_ForNormalCommand_WritesSuccessShort()
    {
        var packet = V113PetPackets.PetCommand(123, 0, command: 7, success: true, food: false);

        Assert.Equal(V113PetPackets.SendPetCommand, BitConverter.ToInt16(packet, 0));
        Assert.Equal(123, BitConverter.ToInt32(packet, 2));
        Assert.Equal(0, packet[6]);
        Assert.Equal(0, packet[7]);
        Assert.Equal(7, packet[8]);
        Assert.Equal(1, BitConverter.ToInt16(packet, 9));
    }

    [Fact]
    public void PetNameChanged_WritesJavaLayout()
    {
        var packet = V113PetPackets.PetNameChanged(123, 0, "Buddy");
        var reader = new PacketReader(packet);

        Assert.Equal(V113PetPackets.SendPetNameChange, reader.ReadShort());
        Assert.Equal(123, reader.ReadInt());
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal("Buddy", reader.ReadMapleString());
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void PetFlagChanged_WritesJavaLayout()
    {
        var packet = V113PetPackets.PetFlagChanged(1001, added: true, PetConstants.ItemPickupFlag);
        var reader = new PacketReader(packet);

        Assert.Equal(V113PetPackets.SendPetFlagChange, reader.ReadShort());
        Assert.Equal(1001, ReadLong(reader));
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(PetConstants.ItemPickupFlag, reader.ReadShort());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void UpdatePet_WritesModifyInventoryPetShape()
    {
        var packet = V113PetPackets.UpdatePet(CreatePet(), cashSlot: 3, expiration: -1);

        Assert.Equal(V113PetPackets.SendModifyInventoryItem, BitConverter.ToInt16(packet, 0));
        Assert.Equal(0, packet[2]);
        Assert.Equal(2, packet[3]);
        Assert.Equal(3, packet[4]);
        Assert.Equal(5, packet[5]);
        Assert.Equal(3, BitConverter.ToInt16(packet, 6));
        Assert.Equal(5000000, BitConverter.ToInt32(packet, 13));
        Assert.Equal(1, packet[17]);
        Assert.Equal(1001, BitConverter.ToInt64(packet, 18));
    }

    [Fact]
    public void ShowPetLevelUp_WritesForeignEffectShape()
    {
        var packet = V113PetPackets.ShowPetLevelUp(123, 0);

        Assert.Equal(V113PetPackets.SendShowForeignEffect, BitConverter.ToInt16(packet, 0));
        Assert.Equal(123, BitConverter.ToInt32(packet, 2));
        Assert.Equal(4, packet[6]);
        Assert.Equal(0, packet[7]);
        Assert.Equal(0, packet[8]);
    }

    private static Pet CreatePet()
        => new(
            petId: 1001,
            itemId: 5000000,
            name: "Kitty",
            level: 2,
            closeness: 3,
            fullness: 80,
            flags: PetConstants.UnpickableFlag,
            position: new Position(10, 30, 1, 7));

    private static long ReadLong(PacketReader reader)
    {
        var low = (uint)reader.ReadInt();
        var high = (uint)reader.ReadInt();
        return unchecked((long)(((ulong)high << 32) | low));
    }
}
