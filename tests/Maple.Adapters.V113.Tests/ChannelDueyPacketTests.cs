using Maple.Adapters.V113.Channel;
using Maple.Core.Duey;
using Maple.Core.Inventory;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelDueyPacketTests
{
    [Fact]
    public void ParseSendPackage_ReadsItemMesoRecipientAndQuickMessage()
    {
        var w = new PacketWriter();
        w.WriteByte((byte)V113DueyClientOperation.SendPackage);
        w.WriteByte((byte)InventoryType.Use);
        w.WriteShort(3);
        w.WriteShort(4);
        w.WriteInt(12_000);
        w.WriteMapleString("Receiver");
        w.WriteByte(1);
        w.WriteMapleString("gift");

        var action = V113DueyPackets.ParseAction(new PacketReader(w.ToArray()));

        Assert.Equal(V113DueyClientOperation.SendPackage, action.Operation);
        Assert.False(action.InvalidInventoryType);
        Assert.NotNull(action.SendRequest);
        Assert.Equal(InventoryType.Use, action.SendRequest!.ItemType);
        Assert.Equal(3, action.SendRequest.ItemSlot);
        Assert.Equal(4, action.SendRequest.Quantity);
        Assert.Equal(12_000, action.SendRequest.Meso);
        Assert.Equal("Receiver", action.SendRequest.RecipientName);
        Assert.True(action.SendRequest.QuickDelivery);
        Assert.Equal("gift", action.SendRequest.Message);
    }

    [Fact]
    public void OpenSecondPassword_MatchesJavaOperation9Layout()
    {
        var packet = V113DueyPackets.OpenSecondPassword();
        var reader = new PacketReader(packet);

        Assert.Equal(V113DueyPackets.SendDuey, reader.ReadShort());
        Assert.Equal(V113DueyPackets.OperationOpenSecondPassword, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void Inbox_WritesPackageListAndZeroPositionItemInfo()
    {
        var packet = V113DueyPackets.Inbox(new[]
        {
            new DueyPackage
            {
                Id = 77,
                SenderName = "Sender",
                RecipientCharacterId = 2,
                Meso = 12_000,
                ExpiresAtUnixMillis = 1_000,
                Message = "gift",
                Item = new ItemRecord
                {
                    Type = (byte)InventoryType.Use,
                    ItemId = 2_000_000,
                    Quantity = 4,
                    Expiration = -1,
                },
            },
        });
        var reader = new PacketReader(packet);

        Assert.Equal(V113DueyPackets.SendDuey, reader.ReadShort());
        Assert.Equal(V113DueyPackets.OperationInbox, reader.ReadByte());
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(77, reader.ReadInt());
        Assert.Equal("Sender", ReadFixedAscii(reader, 15));
        Assert.Equal(12_000, reader.ReadInt());
        reader.Skip(8);
        Assert.Equal(0, reader.ReadShort());
        Assert.StartsWith("13gift", ReadFixedAscii(reader, 193), StringComparison.Ordinal);
        reader.Skip(10);
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(2, reader.ReadByte());
        Assert.Equal(2_000_000, reader.ReadInt());
        Assert.Equal(0, reader.ReadByte());
        reader.Skip(8);
        Assert.Equal(4, reader.ReadShort());
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 3)]
    public void RemovePackage_WritesReceiveOrReturnMode(bool returnedOrDeleted, byte expectedMode)
    {
        var packet = V113DueyPackets.RemovePackage(returnedOrDeleted, 55);
        var reader = new PacketReader(packet);

        Assert.Equal(V113DueyPackets.SendDuey, reader.ReadShort());
        Assert.Equal(V113DueyPackets.OperationRemovePackage, reader.ReadByte());
        Assert.Equal(55, reader.ReadInt());
        Assert.Equal(expectedMode, reader.ReadByte());
        Assert.Equal(0, reader.Remaining);
    }

    private static string ReadFixedAscii(PacketReader reader, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)reader.ReadByte();
        }

        return new string(chars).TrimEnd('\0');
    }
}
