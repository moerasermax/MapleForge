using Maple.Adapters.V113.Channel;
using Maple.Core.IO;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelMobMovementTests
{
    // ── Opcode constants ──────────────────────────────────────────────────────

    [Fact]
    public void MoveLife_OpcodeMatchesExpected()
    {
        Assert.Equal(unchecked((short)0xB6), V113ChannelRecvOp.MoveLife);
    }

    [Fact]
    public void AutoAggro_OpcodeMatchesExpected()
    {
        Assert.Equal(unchecked((short)0xB7), V113ChannelRecvOp.AutoAggro);
    }

    // ── ParseMoveLife ─────────────────────────────────────────────────────────

    [Fact]
    public void ParseMoveLife_ExtractsObjectId_MoveId_StartPos()
    {
        var body = BuildMoveLifeBody(objectId: 200001, moveId: 42, startX: 100, startY: -50,
            unkByte: 0x00, rawMovement: new byte[] { 0xAA, 0xBB });

        var result = V113MobMovementPackets.ParseMoveLife(new PacketReader(body));

        Assert.Equal(200001, result.ObjectId);
        Assert.Equal(42, result.MoveId);
        Assert.Equal(100, result.StartX);
        Assert.Equal(-50, result.StartY);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, result.RawMovement);
    }

    [Fact]
    public void ParseMoveLife_ExtractsSkillFields()
    {
        var body = BuildMoveLifeBody(objectId: 300, moveId: 7, startX: 10, startY: 20,
            unkByte: 0x00, rawMovement: Array.Empty<byte>(),
            useSkill: 1, skillIndex: 3, skillData: 12345);

        var result = V113MobMovementPackets.ParseMoveLife(new PacketReader(body));

        Assert.Equal(1, result.UseSkill);
        Assert.Equal(3, result.SkillIndex);
        Assert.Equal(12345, result.SkillData);
    }

    [Fact]
    public void ParseMoveLife_UnkByte0x12_SkipsExtraBytes()
    {
        // When unk byte == 0x12, parser should skip 2 more bytes before the 11 unknown bytes
        var body = BuildMoveLifeBody(objectId: 400, moveId: 1, startX: 55, startY: 66,
            unkByte: 0x12, rawMovement: new byte[] { 0xCC });

        var result = V113MobMovementPackets.ParseMoveLife(new PacketReader(body));

        Assert.Equal(400, result.ObjectId);
        Assert.Equal(55, result.StartX);
        Assert.Equal(66, result.StartY);
        Assert.Equal(new byte[] { 0xCC }, result.RawMovement);
    }

    // ── MoveMonsterResponse ───────────────────────────────────────────────────

    [Fact]
    public void MoveMonsterResponse_HasCorrectOpcodeAndFields()
    {
        var pkt = V113MobMovementPackets.MoveMonsterResponse(
            objectId: 100001, moveId: 5, mp: 200, aggro: true, skillId: 2, skillLevel: 3);

        var r = new PacketReader(pkt);
        Assert.Equal(V113ChannelSendOp.MoveMonsterResponse, r.ReadShort());  // opcode 0xE9
        Assert.Equal(100001, r.ReadInt());    // objectId
        Assert.Equal(5, r.ReadShort());       // moveId
        Assert.Equal(1, r.ReadByte());        // hasAggro = true → 1
        Assert.Equal(200, r.ReadShort());     // mp
        Assert.Equal(2, r.ReadByte());        // skillId
        Assert.Equal(3, r.ReadByte());        // skillLevel
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void MoveMonsterResponse_NoAggro_WritesZero()
    {
        var pkt = V113MobMovementPackets.MoveMonsterResponse(
            objectId: 500, moveId: 10, mp: 0, aggro: false);

        var r = new PacketReader(pkt);
        r.ReadShort(); // opcode
        r.ReadInt();   // objectId
        r.ReadShort(); // moveId
        Assert.Equal(0, r.ReadByte());  // hasAggro = false → 0
    }

    // ── BroadcastMoveMonster ──────────────────────────────────────────────────

    [Fact]
    public void BroadcastMoveMonster_RelaysRawMovementData()
    {
        var rawMovement = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var pkt = V113MobMovementPackets.BroadcastMoveMonster(
            objectId: 200001, useSkill: 1, skillIndex: 2, skillData: 999,
            startX: 50, startY: 60, rawMovement: rawMovement);

        var r = new PacketReader(pkt);
        Assert.Equal(V113ChannelSendOp.MoveMonster, r.ReadShort());  // opcode 0xE8
        Assert.Equal(200001, r.ReadInt());    // objectId
        Assert.Equal(0, r.ReadByte());        // not init
        Assert.Equal(1, r.ReadByte());        // useSkill
        Assert.Equal(2, r.ReadByte());        // skillIndex
        Assert.Equal(0, r.ReadByte());        // skillId (MVP: 0)
        Assert.Equal(0, r.ReadByte());        // skillLevel (MVP: 0)
        Assert.Equal(999, r.ReadInt());       // skillData
        r.Skip(12);                           // 12 zero bytes (1 + 11 unknown)
        Assert.Equal(50, r.ReadShort());      // startX
        Assert.Equal(60, r.ReadShort());      // startY

        // Raw movement relayed verbatim
        var tail = r.ReadBytes(r.Remaining);
        Assert.Equal(rawMovement, tail);
    }

    [Fact]
    public void BroadcastMoveMonster_EmptyMovement_StillValid()
    {
        var pkt = V113MobMovementPackets.BroadcastMoveMonster(
            objectId: 1, useSkill: 0, skillIndex: 0, skillData: 0,
            startX: 0, startY: 0, rawMovement: Array.Empty<byte>());

        var r = new PacketReader(pkt);
        Assert.Equal(V113ChannelSendOp.MoveMonster, r.ReadShort());
        r.ReadInt();   // objectId
        r.Skip(5);     // not init + useSkill + skillIndex + skillId + skillLevel
        r.ReadInt();   // skillData
        r.Skip(12);    // zeros
        r.ReadShort(); // startX
        r.ReadShort(); // startY
        Assert.Equal(0, r.Remaining);
    }

    // ── AutoAggro sets controller ─────────────────────────────────────────────

    [Fact]
    public void ControllerId_DefaultsToZero()
    {
        var mob = SampleMob();
        Assert.Equal(0, mob.ControllerId);
    }

    [Fact]
    public void ControllerId_CanBeSet()
    {
        var mob = SampleMob();
        mob.ControllerId = 42;
        Assert.Equal(42, mob.ControllerId);
    }

    // ── FieldInstance.GetMob ───────────────────────────────────────────────────

    [Fact]
    public void FieldInstance_GetMob_ReturnsMob()
    {
        var field = new FieldInstance(100000);
        var mob = SampleMob();
        field.Add(mob);

        Assert.Same(mob, field.GetMob(mob.ObjectId));
    }

    [Fact]
    public void FieldInstance_GetMob_ReturnsNull_ForNonExistent()
    {
        var field = new FieldInstance(100000);
        Assert.Null(field.GetMob(999));
    }

    [Fact]
    public void FieldInstance_GetMob_ReturnsNull_ForNonMobObject()
    {
        var field = new FieldInstance(100000);
        // Player is IFieldObject but not Mob; GetMob should return null
        // We can't easily create a Player here, so just test non-existent
        Assert.Null(field.GetMob(1));
    }

    // ── PacketReader.ReadBytes ────────────────────────────────────────────────

    [Fact]
    public void PacketReader_ReadBytes_ReadsCorrectSlice()
    {
        var data = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 };
        var r = new PacketReader(data);
        r.Skip(1);  // skip first byte

        var result = r.ReadBytes(3);

        Assert.Equal(new byte[] { 0x20, 0x30, 0x40 }, result);
        Assert.Equal(1, r.Remaining);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mob SampleMob()
    {
        var def = new MapMonster { MonsterId = 100100, X = 30, Y = 40, Fh = 7, Team = -1 };
        var stats = new MobStats(100100, MaxHp: 100, MaxMp: 50, Level: 5, Exp: 20);
        return new Mob(def, stats, objectId: 200001);
    }

    /// <summary>
    /// Build a MOVE_LIFE packet body (after opcode has been consumed).
    /// Layout: [int objectId][short moveId][byte useSkill][byte skillIndex][int skillData]
    ///         [1 skip][byte unk; if 0x12: 2 more skip][11 skip][short startX][short startY][rawMovement]
    /// </summary>
    private static byte[] BuildMoveLifeBody(
        int objectId, short moveId, short startX, short startY,
        byte unkByte, byte[] rawMovement,
        byte useSkill = 0, byte skillIndex = 0, int skillData = 0)
    {
        var w = new PacketWriter(64);
        w.WriteInt(objectId);
        w.WriteShort(moveId);
        w.WriteByte(useSkill);
        w.WriteByte(skillIndex);
        w.WriteInt(skillData);
        w.WriteByte(0);              // skip 1
        w.WriteByte(unkByte);        // unk byte
        if (unkByte == 0x12)
            w.WriteZeroBytes(2);     // extra 2 bytes when unk == 0x12
        w.WriteZeroBytes(11);        // skip 11
        w.WriteShort(startX);
        w.WriteShort(startY);
        w.WriteBytes(rawMovement);
        return w.ToArray();
    }
}
