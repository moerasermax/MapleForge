using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal sealed record V113SpecialMoveRequest(
    short OldX,
    short OldY,
    int SkillId,
    byte SkillLevel,
    short? X,
    short? Y,
    bool? FaceLeft);

internal sealed record V113SkillHandleResult(
    int SourceId,
    byte[]? Packet,
    SkillCastResult? Cast,
    CancelBuffResult? Cancel);

/// <summary>v113 技能/buff 封包。對照 Java PlayerHandler.SpecialMove/CancelBuffHandler 與 MaplePacketCreator.giveBuff/cancelBuff。</summary>
internal static class V113SkillPackets
{
    public const short SpecialMoveRecvOp = 0x55;
    public const short CancelBuffRecvOp = 0x56;
    public const short SkillEffectRecvOp = 0x57;

    public const short GiveBuffOp = 0x1E;
    public const short CancelBuffOp = 0x1F;
    public const short UpdateSkillsOp = 0x22;
    public const short SkillUseResultOp = 0x23;
    public const short RemoteSkillEffectOp = unchecked((short)0xB6);
    public const short RemoteCancelSkillEffectOp = unchecked((short)0xB7);
    public const short GiveForeignBuffOp = unchecked((short)0xC0);
    public const short CancelForeignBuffOp = unchecked((short)0xC1);

    public static V113SpecialMoveRequest ParseSpecialMove(PacketReader reader)
    {
        var oldX = reader.ReadShort();
        var oldY = reader.ReadShort();
        var skillId = reader.ReadInt();
        var skillLevel = reader.ReadByte();

        short? x = null;
        short? y = null;
        bool? faceLeft = null;
        if (reader.Remaining is 5 or 7)
        {
            x = reader.ReadShort();
            y = reader.ReadShort();
            faceLeft = reader.ReadByte() == 0;
        }

        return new V113SpecialMoveRequest(oldX, oldY, skillId, skillLevel, x, y, faceLeft);
    }

    public static int ParseCancelBuff(PacketReader reader)
        => reader.ReadInt();

    public static byte[] GiveBuff(int buffId, int durationMilliseconds, IReadOnlyList<BuffStatValue> statups, MapleStatEffect? effect = null)
    {
        var w = new PacketWriter(32 + statups.Count * 10);
        w.WriteShort(GiveBuffOp);
        WriteBuffMask(w, statups.Select(static s => s.Stat));

        foreach (var statup in statups)
        {
            w.WriteShort((short)statup.Value);
            w.WriteInt(buffId);
            w.WriteInt(durationMilliseconds);
        }

        w.WriteShort(0);
        w.WriteShort(0);
        if (effect is null || (!effect.IsCombo && !effect.IsFinalAttack))
        {
            w.WriteByte(0);
        }

        return w.ToArray();
    }

    public static byte[] CancelBuff(IReadOnlyList<MapleBuffStat> stats)
    {
        var w = new PacketWriter(24);
        w.WriteShort(CancelBuffOp);
        WriteBuffMask(w, stats);
        w.WriteByte(3);
        return w.ToArray();
    }

    public static byte[] UpdateSkill(CharacterSkillRecord skill)
    {
        var w = new PacketWriter(32);
        w.WriteShort(UpdateSkillsOp);
        w.WriteByte(1);
        w.WriteShort(1);
        w.WriteInt(skill.SkillId);
        w.WriteInt(skill.Level);
        w.WriteInt(skill.MasterLevel);
        w.WriteLong(GetTime(skill.Expiration));
        w.WriteByte(4);
        return w.ToArray();
    }

    public static void AddCharacterSkillInfo(PacketWriter w, Character chr)
    {
        w.WriteShort(chr.Skills.Count);
        foreach (var skill in chr.Skills)
        {
            w.WriteInt(skill.SkillId);
            w.WriteInt(skill.Level);
            if (MapleSkill.IsFourthJobSkillId(skill.SkillId, skill.MasterLevel))
            {
                w.WriteInt(skill.MasterLevel);
            }
        }
    }

    public static void WriteBuffMask(PacketWriter w, IEnumerable<MapleBuffStat> stats)
    {
        Span<int> mask = stackalloc int[4];
        foreach (var stat in stats)
        {
            mask[stat.GetMaskPosition()] |= stat.GetMaskValue();
        }

        for (var i = 0; i < mask.Length; i++)
        {
            w.WriteInt(mask[i]);
        }
    }

    private static long GetTime(long expiration)
    {
        const long WindowsEpochOffset = 116444736000000000L;
        if (expiration < 0)
        {
            return WindowsEpochOffset + expiration;
        }

        return WindowsEpochOffset + (expiration * 10000);
    }
}

internal static class V113SkillMoveHandler
{
    public static V113SkillHandleResult HandleSpecialMove(
        PacketReader reader,
        Player player,
        SkillService skillService,
        DateTimeOffset now)
    {
        var request = V113SkillPackets.ParseSpecialMove(reader);
        var result = skillService.Cast(player, request.SkillId, request.SkillLevel, now);
        var packet = result.Status == SkillCastStatus.Success && result.AppliedBuff is not null && result.Effect is not null
            ? V113SkillPackets.GiveBuff(request.SkillId, result.AppliedBuff.DurationMilliseconds, result.AppliedBuff.Stats, result.Effect)
            : null;

        return new V113SkillHandleResult(request.SkillId, packet, result, null);
    }

    public static V113SkillHandleResult HandleCancelBuff(
        PacketReader reader,
        Player player,
        SkillService skillService)
    {
        var sourceId = V113SkillPackets.ParseCancelBuff(reader);
        var result = skillService.CancelBuff(player, sourceId);
        var packet = result.Status == CancelBuffStatus.Success
            ? V113SkillPackets.CancelBuff(result.Cancellations.SelectMany(static c => c.Stats).Distinct().ToArray())
            : null;

        return new V113SkillHandleResult(sourceId, packet, null, result);
    }

    public static IReadOnlyList<byte[]> CancelExpiredBuffs(Player player, SkillService skillService, DateTimeOffset now)
        => skillService.CancelExpiredBuffs(player, now)
            .Select(static c => V113SkillPackets.CancelBuff(c.Stats))
            .ToArray();
}
