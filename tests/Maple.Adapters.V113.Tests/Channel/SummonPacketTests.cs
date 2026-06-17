using Maple.Adapters.V113.Channel;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests.Channel;

public sealed class SummonPacketTests
{
    [Fact]
    public void Opcodes_MatchTaskTable()
    {
        Assert.Equal(unchecked((short)0xAC), V113SummonPackets.MoveSummonRecvOp);
        Assert.Equal(unchecked((short)0xAD), V113SummonPackets.SummonAttackRecvOp);
        Assert.Equal(unchecked((short)0xAE), V113SummonPackets.DamageSummonRecvOp);
        Assert.Equal(unchecked((short)0xAF), V113SummonPackets.SubSummonRecvOp);

        Assert.Equal(unchecked((short)0xAA), V113SummonPackets.SpawnSummonOp);
        Assert.Equal(unchecked((short)0xAB), V113SummonPackets.RemoveSummonOp);
        Assert.Equal(unchecked((short)0xAD), V113SummonPackets.SummonAttackOp);
        Assert.Equal(unchecked((short)0xAE), V113SummonPackets.MoveSummonOp);
        Assert.Equal(unchecked((short)0xAF), V113SummonPackets.DamageSummonOp);
    }

    [Fact]
    public void SpawnSummon_WritesJavaCreatorCoreFields()
    {
        var pkt = V113SummonPackets.SpawnSummon(CreateSummon(), ownerLevel: 30);
        var r = new PacketReader(pkt);

        Assert.Equal(V113SummonPackets.SpawnSummonOp, r.ReadShort());
        Assert.Equal(7, r.ReadInt());
        Assert.Equal(200001, r.ReadInt());
        Assert.Equal(1321007, r.ReadInt());
        Assert.Equal((byte)29, r.ReadByte());
        Assert.Equal((byte)1, r.ReadByte());
        Assert.Equal((short)100, r.ReadShort());
        Assert.Equal((short)200, r.ReadShort());
        Assert.Equal((byte)4, r.ReadByte());
        Assert.Equal((short)0, r.ReadShort());
        Assert.Equal((byte)SummonMovementType.Follow, r.ReadByte());
        Assert.Equal((byte)2, r.ReadByte());
        Assert.Equal((byte)0, r.ReadByte());
        Assert.All(r.ReadBytes(r.Remaining), b => Assert.Equal((byte)0, b));
    }

    [Fact]
    public void RemoveSummon_WritesOwnerObjectAndAnimation()
    {
        var pkt = V113SummonPackets.RemoveSummon(CreateSummon(), animated: true);
        var r = new PacketReader(pkt);

        Assert.Equal(V113SummonPackets.RemoveSummonOp, r.ReadShort());
        Assert.Equal(7, r.ReadInt());
        Assert.Equal(200001, r.ReadInt());
        Assert.Equal((byte)4, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void MoveSummon_RelaysStartPositionAndRawMovement()
    {
        var raw = new byte[] { 0x01, 0x00, 0x64, 0x00 };
        var pkt = V113SummonPackets.MoveSummon(ownerId: 7, objectId: 200001, startX: 10, startY: 20, raw);
        var r = new PacketReader(pkt);

        Assert.Equal(V113SummonPackets.MoveSummonOp, r.ReadShort());
        Assert.Equal(7, r.ReadInt());
        Assert.Equal(200001, r.ReadInt());
        Assert.Equal((short)10, r.ReadShort());
        Assert.Equal((short)20, r.ReadShort());
        Assert.Equal(raw, r.ReadBytes(r.Remaining));
    }

    [Fact]
    public void SummonAttack_WritesTargetsWithDamage()
    {
        var targets = new[]
        {
            new V113SummonAttackTarget(100001, 1234),
            new V113SummonAttackTarget(100002, 5678),
        };

        var pkt = V113SummonPackets.SummonAttack(
            ownerId: 7,
            summonObjectId: 200001,
            animation: 3,
            targets: targets,
            ownerLevel: 30);
        var r = new PacketReader(pkt);

        Assert.Equal(V113SummonPackets.SummonAttackOp, r.ReadShort());
        Assert.Equal(7, r.ReadInt());
        Assert.Equal(200001, r.ReadInt());
        Assert.Equal((byte)29, r.ReadByte());
        Assert.Equal((byte)3, r.ReadByte());
        Assert.Equal((byte)2, r.ReadByte());
        Assert.Equal(100001, r.ReadInt());
        Assert.Equal((byte)0x07, r.ReadByte());
        Assert.Equal(1234, r.ReadInt());
        Assert.Equal(100002, r.ReadInt());
        Assert.Equal((byte)0x07, r.ReadByte());
        Assert.Equal(5678, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void DamageSummon_WritesJavaCreatorLayout()
    {
        var pkt = V113SummonPackets.DamageSummon(
            ownerId: 7,
            skillId: 3111002,
            damage: 42,
            unknown: 2,
            monsterIdFrom: 100100);
        var r = new PacketReader(pkt);

        Assert.Equal(V113SummonPackets.DamageSummonOp, r.ReadShort());
        Assert.Equal(7, r.ReadInt());
        Assert.Equal(3111002, r.ReadInt());
        Assert.Equal((byte)2, r.ReadByte());
        Assert.Equal(42, r.ReadInt());
        Assert.Equal(100100, r.ReadInt());
        Assert.Equal((byte)0, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void ParseMoveSummon_ReadsObjectStartPositionAndBlob()
    {
        var raw = new byte[] { 0x01, 0x02, 0x03 };
        var body = new PacketWriter()
            .WriteInt(200001)
            .WriteShort(10)
            .WriteShort(20)
            .WriteBytes(raw)
            .ToArray();

        var request = V113SummonPackets.ParseMoveSummon(new PacketReader(body));

        Assert.Equal(200001, request.ObjectId);
        Assert.Equal((short)10, request.StartX);
        Assert.Equal((short)20, request.StartY);
        Assert.Equal(raw, request.RawMovement);
    }

    [Fact]
    public void ParseDamageSummon_ReadsDamageAndMonsterId()
    {
        var body = new PacketWriter()
            .WriteByte(4)
            .WriteInt(99)
            .WriteInt(100100)
            .ToArray();

        var request = V113SummonPackets.ParseDamageSummon(new PacketReader(body));

        Assert.Equal((byte)4, request.Unknown);
        Assert.Equal(99, request.Damage);
        Assert.Equal(100100, request.MonsterIdFrom);
    }

    private static Summon CreateSummon()
        => new(
            objectId: 200001,
            skillId: 1321007,
            skillLevel: 10,
            ownerId: 7,
            hp: 100,
            movementType: SummonMovementType.Follow,
            position: new Position(100, 200, 4, 9));
}
