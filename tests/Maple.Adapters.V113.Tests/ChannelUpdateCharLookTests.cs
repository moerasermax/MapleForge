using Maple.Adapters.V113.Channel;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>P091：UPDATE_CHAR_LOOK（0xBE）對照 Java <c>MaplePacketCreator.updateCharLook</c>（unverified）。</summary>
public sealed class ChannelUpdateCharLookTests
{
    [Fact]
    public void UpdateCharLook_WritesJavaLayout()
    {
        var chr = new Character
        {
            Id = 42,
            Name = "Look",
            Gender = 1,
            SkinColor = 2,
            Face = 21000,
            Hair = 31000,
        };
        chr.Equips.Add(new EquipEntry { Position = -5, ItemId = 1040002 });
        var player = new Player(chr, new Position(0, 0, 0, 0));

        var r = new PacketReader(V113MapPackets.UpdateCharLook(player));

        Assert.Equal(unchecked((short)0xBE), r.ReadShort());
        Assert.Equal(42, r.ReadInt());
        Assert.Equal(1, r.ReadByte());
        // addCharLook
        Assert.Equal(1, r.ReadByte());       // gender
        Assert.Equal(2, r.ReadByte());       // skin
        Assert.Equal(21000, r.ReadInt());    // face
        Assert.Equal(1, r.ReadByte());       // mega=false → 1
        Assert.Equal(31000, r.ReadInt());    // hair
        Assert.Equal(5, r.ReadByte());       // slot 5
        Assert.Equal(1040002, r.ReadInt());
        Assert.Equal(0xFF, r.ReadByte());
        Assert.Equal(0xFF, r.ReadByte());
        Assert.Equal(0, r.ReadInt());        // cash weapon
        Assert.Equal(0, r.ReadInt());        // pets
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        // addRingInfo（空清單）
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.ReadInt());
        // addMarriageRingLook（無婚戒）
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }
}
