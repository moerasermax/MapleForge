using Maple.Adapters.V113.Channel;
using Maple.Core.IO;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>P110：對照 Java <c>MonsterStatus</c> 與 <c>MobPacket</c> 狀態編碼。</summary>
public sealed class ChannelMonsterStatusPacketTests
{
    [Theory]
    [InlineData(MonsterStatus.Watk, 3, 0x1, 3)]
    [InlineData(MonsterStatus.Stun, 3, 0x80, 3)]
    [InlineData(MonsterStatus.Empty, 3, 0x08000000, 33)]
    [InlineData(MonsterStatus.Doom, 3, 0x10000, 18)]
    [InlineData(MonsterStatus.Mbs32, 2, 0x1, 28)]
    [InlineData(MonsterStatus.MagicCrash, 2, 0x10, 2)]
    public void Codec_MatchesJavaConstructors(MonsterStatus status, int position, int bit, int order)
    {
        Assert.Equal(position, V113MonsterStatusCodec.Position(status));
        Assert.Equal(bit, V113MonsterStatusCodec.Bit(status));
        Assert.Equal(order, V113MonsterStatusCodec.Order(status));
    }

    [Fact]
    public void ApplyMonsterStatus_SingleStun_MatchesJavaProcessStatSet()
    {
        var packet = V113MonsterStatusCodec.ApplyMonsterStatus(
            100001,
            [new MonsterStatusEffect(MonsterStatus.Stun, 1, 1111005, null, null)]);
        var r = new PacketReader(packet);

        Assert.Equal(V113MonsterStatusCodec.ApplyMonsterStatusOp, r.ReadShort());
        Assert.Equal(100001, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0x80, r.ReadInt());
        Assert.Equal(1, r.ReadShort());       // x
        Assert.Equal(1111005, r.ReadInt());   // skill
        Assert.Equal(1, r.ReadShort());       // !isDefault
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void ApplyMonsterStatus_MobSkillSource_WritesIdAndLevelShorts()
    {
        var packet = V113MonsterStatusCodec.ApplyMonsterStatus(
            100001,
            [new MonsterStatusEffect(MonsterStatus.Watk, 30, 0, new MobSkillSource(100, 2), null)]);
        var r = new PacketReader(packet);
        r.ReadShort();
        r.ReadInt();
        for (var i = 0; i < 4; i++) r.ReadInt();

        Assert.Equal(30, r.ReadShort());
        Assert.Equal(100, r.ReadShort());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(1, r.ReadShort());
    }

    [Fact]
    public void CancelMonsterStatus_WritesMaskAndTrailingZero()
    {
        Assert.Equal(
            new byte[]
            {
                0xEC, 0x00, 0xA1, 0x86, 0x01, 0x00,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 0x02, 0, 0,
                0, 0, 0, 0,
            },
            V113MonsterStatusCodec.CancelMonsterStatus(100001, [MonsterStatus.Stun, MonsterStatus.Poison]));
    }

    [Fact]
    public void SpawnStatus_WithStun_SortsStunBeforeEmpty()
    {
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, 100, MaxMp: 0, Level: 1, Exp: 1),
            objectId: 100001);
        mob.TryApplyStatus(new MonsterStatusEffect(MonsterStatus.Stun, 1, 1111005, null, null));
        var w = new PacketWriter();

        V113MonsterStatusCodec.WriteSpawnStatus(w, mob);
        var r = new PacketReader(w.ToArray());

        for (var i = 0; i < 3; i++) Assert.Equal(0, r.ReadInt());
        Assert.Equal(0x08000080, r.ReadInt());
        Assert.Equal(1, r.ReadShort());
        Assert.Equal(1111005, r.ReadInt());
        Assert.Equal(1, r.ReadShort());
        Assert.Equal(0, r.ReadInt()); // EMPTY payload
        Assert.Equal(0, r.Remaining);
    }
}
