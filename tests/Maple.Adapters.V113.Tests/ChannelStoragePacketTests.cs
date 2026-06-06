using Maple.Adapters.V113.Channel;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Storage;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelStoragePacketTests
{
    [Fact]
    public void ParseStoreRequest_ReadsSlotItemAndQuantity()
    {
        var w = new PacketWriter();
        w.WriteByte((byte)StorageClientMode.Store);
        w.WriteShort(3);
        w.WriteInt(2000000);
        w.WriteShort(7);

        var req = V113StoragePackets.Parse(new PacketReader(w.ToArray()));

        Assert.Equal(StorageClientMode.Store, req.Mode);
        Assert.Equal(InventoryType.Use, req.Type);
        Assert.Equal(3, req.InventorySlot);
        Assert.Equal(2000000, req.ItemId);
        Assert.Equal(7, req.Quantity);
    }

    [Fact]
    public void ParseTakeOutRequest_ReadsTypeAndTypeLocalSlot()
    {
        byte[] body = { (byte)StorageClientMode.TakeOut, (byte)InventoryType.Etc, 2 };

        var req = V113StoragePackets.Parse(new PacketReader(body));

        Assert.Equal(StorageClientMode.TakeOut, req.Mode);
        Assert.True(req.HasValidType);
        Assert.Equal(InventoryType.Etc, req.Type);
        Assert.Equal(2, req.StorageSlot);
    }

    [Fact]
    public void OpenStorage_EmptyStorage_MatchesJavaHeaderLayout()
    {
        var storage = StorageBox.Hydrate(new AccountStorage { Slots = 4, Meso = 12345 });

        var pkt = V113StoragePackets.Open(1002005, storage);
        var r = new PacketReader(pkt);

        Assert.Equal(V113StoragePackets.SendOpenStorageOpcode, r.ReadShort());
        Assert.Equal(0x16, r.ReadByte());
        Assert.Equal(1002005, r.ReadInt());
        Assert.Equal(4, r.ReadByte());
        Assert.Equal(0x7E, r.ReadShort());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(12345, r.ReadInt());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void StoreStorage_WritesTypeBitfieldAndZeroPositionItemInfo()
    {
        var storage = StorageBox.Hydrate(new AccountStorage
        {
            Slots = 4,
            Items =
            {
                new ItemRecord { Type = (byte)InventoryType.Use, ItemId = 2000000, Slot = 0, Quantity = 5 },
            },
        });

        var pkt = V113StoragePackets.Stored(storage, InventoryType.Use);
        var r = new PacketReader(pkt);

        Assert.Equal(V113StoragePackets.SendOpenStorageOpcode, r.ReadShort());
        Assert.Equal(0x0D, r.ReadByte());
        Assert.Equal(4, r.ReadByte());
        Assert.Equal(8, r.ReadShort());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(2, r.ReadByte());
        Assert.Equal(2000000, r.ReadInt());
        Assert.Equal(0, r.ReadByte());
        r.Skip(8);
        Assert.Equal(5, r.ReadShort());
        Assert.Equal(string.Empty, r.ReadMapleString());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void MesoStorage_WritesUpdatedStorageMeso()
    {
        var storage = StorageBox.Hydrate(new AccountStorage { Slots = 8, Meso = 777 });

        var pkt = V113StoragePackets.Meso(storage);
        var r = new PacketReader(pkt);

        Assert.Equal(V113StoragePackets.SendOpenStorageOpcode, r.ReadShort());
        Assert.Equal(0x13, r.ReadByte());
        Assert.Equal(8, r.ReadByte());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(777, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }
}
