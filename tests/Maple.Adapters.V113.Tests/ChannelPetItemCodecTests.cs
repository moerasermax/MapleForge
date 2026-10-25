using Maple.Adapters.V113.Channel;
using Maple.Core.Inventory;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

/// <summary>P198：寵物道具 itemInfo（Java addItemInfo pet 分支／addPetItemInfo；Java-source candidate／unverified）。</summary>
public sealed class ChannelPetItemCodecTests
{
    [Fact]
    public void PetItem_WritesType3_UniqueId_NameLevelClosenessFullness()
    {
        var item = new Item { ItemId = 5000000, UniqueId = 777, Owner = "Pi", PetLevel = 12, PetCloseness = 345, PetFullness = 60, Flag = 4, Expiration = -1 };
        var w = new PacketWriter();

        Assert.True(V113PetItemCodec.TryWrite(w, item));

        var r = new PacketReader(w.ToArray());
        Assert.Equal(3, r.ReadByte());
        Assert.Equal(5000000, r.ReadInt());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(777, ReadLong(r));
        Assert.Equal(150842304000000000L, ReadLong(r)); // Java MAX_TIME（無期限）
        Assert.Equal("Pi", System.Text.Encoding.ASCII.GetString(r.ReadBytes(13)).TrimEnd('\0'));
        Assert.Equal(12, r.ReadByte());
        Assert.Equal(345, r.ReadShort());
        Assert.Equal(60, r.ReadByte());
        Assert.Equal(150842304000000000L, ReadLong(r));
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(4, r.ReadShort());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void NonPet_NotHandled_AndJavaTimeFormula()
    {
        Assert.False(V113PetItemCodec.TryWrite(new PacketWriter(), new Item { ItemId = 2000000 }));
        Assert.Equal(116444592000000000L + (1_700_000_000L * 10_000_000), V113PetItemCodec.JavaTime(1_700_000_000_123));
    }

    [Fact]
    public void ModifyInventoryAdd_PetItem_UsesPetFormat()
    {
        var packet = V113ItemUsePackets.ModifyInventoryAdd(InventoryType.Cash, new Item { ItemId = 5000001, Slot = 2, UniqueId = 9 });

        Assert.Equal(3, packet[8]); // [op 2][0][1][mode 0][type 5][slot 2 bytes] → itemType
    }

    private static long ReadLong(PacketReader r) => (uint)r.ReadInt() | ((long)r.ReadInt() << 32);
}
