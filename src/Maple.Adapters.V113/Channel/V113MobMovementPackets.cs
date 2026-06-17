using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

/// <summary>MOVE_LIFE (0xB6) 解析結果。</summary>
internal readonly record struct V113MoveLifeData(
    int ObjectId,
    short MoveId,
    byte UseSkill,
    byte SkillIndex,
    int SkillData,
    short StartX,
    short StartY,
    byte[] RawMovement);

/// <summary>
/// v113 怪物移動封包：解析 c2s MOVE_LIFE、編碼 s2c MoveMonsterResponse / BroadcastMoveMonster。
/// 對照 Java MobHandler.MoveMonster + MobPacket.moveMonsterResponse / moveMonster。
/// </summary>
internal static class V113MobMovementPackets
{
    /// <summary>
    /// 解析 c2s MOVE_LIFE (0xB6)。
    /// 格式：[int objectId][short moveId][byte useSkill][byte skillIndex][int skillData]
    ///       [skip 1][byte unk; if unk==0x12 skip 2 more][skip 11][short startX][short startY]
    ///       [remaining = raw movement data]
    /// </summary>
    public static V113MoveLifeData ParseMoveLife(PacketReader reader)
    {
        var objectId = reader.ReadInt();
        var moveId = reader.ReadShort();
        var useSkill = reader.ReadByte();
        var skillIndex = reader.ReadByte();
        var skillData = reader.ReadInt();
        reader.Skip(1);                        // unknown byte
        var unk = reader.ReadByte();
        if (unk == 0x12)
            reader.Skip(2);                    // extra 2 bytes when unk == 0x12
        reader.Skip(11);                       // unknown 11 bytes
        var startX = reader.ReadShort();
        var startY = reader.ReadShort();
        var rawMovement = reader.ReadBytes(reader.Remaining);

        return new V113MoveLifeData(objectId, moveId, useSkill, skillIndex, skillData, startX, startY, rawMovement);
    }

    /// <summary>
    /// s2c MOVE_MONSTER_RESPONSE (0xE9)。
    /// 格式：[short opcode][int objectId][short moveId][byte hasAggro][short mp][byte skillId][byte skillLevel]
    /// </summary>
    public static byte[] MoveMonsterResponse(int objectId, short moveId, int mp, bool aggro, byte skillId = 0, byte skillLevel = 0)
    {
        var w = new PacketWriter(16);
        w.WriteShort(V113ChannelSendOp.MoveMonsterResponse);
        w.WriteInt(objectId);
        w.WriteShort(moveId);
        w.WriteByte(aggro ? 1 : 0);
        w.WriteShort(mp);
        w.WriteByte(skillId);
        w.WriteByte(skillLevel);
        return w.ToArray();
    }

    /// <summary>
    /// s2c MOVE_MONSTER broadcast (0xE8)。
    /// 格式：[short opcode][int objectId][byte 0(not init)][byte useSkill][byte skillIndex]
    ///       [byte 0(skillId)][byte 0(skillLevel)][int skillData][skip 12 zeros][short startX][short startY]
    ///       [raw movement bytes]
    /// </summary>
    public static byte[] BroadcastMoveMonster(int objectId, byte useSkill, byte skillIndex, int skillData, short startX, short startY, byte[] rawMovement)
    {
        var w = new PacketWriter(32 + rawMovement.Length);
        w.WriteShort(V113ChannelSendOp.MoveMonster);
        w.WriteInt(objectId);
        w.WriteByte(0);            // not init
        w.WriteByte(useSkill);
        w.WriteByte(skillIndex);
        w.WriteByte(0);            // skillId (MVP: skip mob skill application)
        w.WriteByte(0);            // skillLevel
        w.WriteInt(skillData);
        w.WriteZeroBytes(12);      // 1 unknown + 11 unknown
        w.WriteShort(startX);
        w.WriteShort(startY);
        w.WriteBytes(rawMovement);
        return w.ToArray();
    }
}
