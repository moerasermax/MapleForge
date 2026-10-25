using Maple.Adapters.V113.Channel;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>P126：對照 Java giveDebuff / giveForeignDebuff / cancelDebuff / cancelForeignDebuff。</summary>
public sealed class ChannelDiseasePacketTests
{
    private static readonly ActiveDisease Stun = new(PlayerDisease.Stun, 1, 123, 2, DateTimeOffset.UnixEpoch, 5000);

    [Fact]
    public void GiveDebuff_StunInSecondMask()
    {
        var r = new PacketReader(V113DiseasePackets.GiveDebuff(Stun));

        Assert.Equal(0x1E, r.ReadShort());
        Assert.Equal(0L, ReadLong(r));
        Assert.Equal(0x2000000000000L, ReadLong(r));
        Assert.Equal(1, r.ReadShort());
        Assert.Equal(123, r.ReadShort());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(5000, r.ReadInt());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(900, r.ReadShort());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void GiveForeignDebuff_PoisonHasExtraShort()
    {
        var poison = Stun with { Disease = PlayerDisease.Poison, SkillId = 125 };
        var r = new PacketReader(V113DiseasePackets.GiveForeignDebuff(7, poison));

        Assert.Equal(unchecked((short)0xC0), r.ReadShort());
        Assert.Equal(7, r.ReadInt());
        ReadLong(r);
        Assert.Equal(0x4000000000000L, ReadLong(r));
        Assert.Equal(0, r.ReadShort());   // skillid == 125 額外 short
        Assert.Equal(125, r.ReadShort());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(900, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void CancelDebuff_FirstDiseaseUsesFirstMask()
    {
        var r = new PacketReader(V113DiseasePackets.CancelDebuff(PlayerDisease.Freeze));

        Assert.Equal(0x1F, r.ReadShort());
        Assert.Equal(0x8000000000000L, ReadLong(r));
        Assert.Equal(0L, ReadLong(r));
        Assert.Equal(1, r.ReadByte());
    }

    [Fact]
    public void CancelForeignDebuff_CurseHighBit()
    {
        var r = new PacketReader(V113DiseasePackets.CancelForeignDebuff(7, PlayerDisease.Curse));

        Assert.Equal(unchecked((short)0xC1), r.ReadShort());
        Assert.Equal(7, r.ReadInt());
        Assert.Equal(0L, ReadLong(r));
        Assert.Equal(long.MinValue, ReadLong(r));
        Assert.Equal(0, r.Remaining);
    }

    private static long ReadLong(PacketReader r) => (uint)r.ReadInt() | ((long)r.ReadInt() << 32);
}
