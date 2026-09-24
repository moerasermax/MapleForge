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
    CancelBuffResult? Cancel,
    byte[]? CooldownPacket = null,
    byte[]? StatsPacket = null,
    V113SpecialMoveRequest? Request = null,
    IReadOnlyList<byte[]>? CooldownResetPackets = null);

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
    public const short CooldownOp = unchecked((short)0xE3); // COOLDOWN（send.properties 0xE3）

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

    /// <summary>對照 Java <c>MaplePacketCreator.skillCooldown(sid, time)</c>：通知客戶端技能冷卻秒數，
    /// <c>seconds == 0</c> 表示冷卻結束。</summary>
    public static byte[] SkillCooldown(int skillId, int seconds)
    {
        var w = new PacketWriter(8);
        w.WriteShort(CooldownOp);
        w.WriteInt(skillId);
        w.WriteShort((short)seconds);
        return w.ToArray();
    }

    /// <summary>P093：對照 Java <c>showOwnBuffEffect(skillid, effectid)</c>（direction 3 不寫）：
    /// <c>SHOW_ITEM_GAIN_INCHAT(0xC7)</c> + effectId + skillId + 1 + 1。</summary>
    public static byte[] ShowOwnBuffEffect(int skillId, byte effectId, byte direction = 3)
    {
        var w = new PacketWriter(10);
        w.WriteShort(V113ChannelSendOp.ShowItemGainInChat);
        w.WriteByte(effectId);
        w.WriteInt(skillId);
        w.WriteByte(1);
        w.WriteByte(1);
        // P094：Java `if (direction != 3 || skillid == 1320006) write(direction)`。
        if (direction != 3 || skillId == SkillService.BerserkSkillId)
        {
            w.WriteByte(direction);
        }

        return w.ToArray();
    }

    /// <summary>P093：對照 Java <c>showBuffeffect(cid, skillid, effectid)</c>：
    /// <c>SHOW_FOREIGN_EFFECT(0xBF)</c> + cid + effectId + skillId + 1 + 1。</summary>
    public static byte[] ShowForeignBuffEffect(int characterId, int skillId, byte effectId, byte direction = 3)
    {
        var w = new PacketWriter(15);
        w.WriteShort(V113ChannelSendOp.ShowForeignEffect);
        w.WriteInt(characterId);
        w.WriteByte(effectId);
        w.WriteInt(skillId);
        w.WriteByte(1);
        w.WriteByte(1);
        if (direction != 3 || skillId == SkillService.BerserkSkillId)
        {
            w.WriteByte(direction);
        }

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
        var mpBefore = player.Mp;
        var result = skillService.Cast(player, request.SkillId, request.SkillLevel, now);
        var packet = result.Status == SkillCastStatus.Success && result.AppliedBuff is not null && result.Effect is not null
            ? V113SkillPackets.GiveBuff(request.SkillId, result.AppliedBuff.DurationMilliseconds, result.AppliedBuff.Stats, result.Effect)
            : null;

        // 對照 Java PlayerHandler.SpecialMove：冷卻中被拒回 enableActions 解鎖客戶端；
        // 成功登記冷卻則送 skillCooldown(skillId, 秒數) 讓客戶端圖示進入冷卻。
        var cooldownPacket = result.Status == SkillCastStatus.OnCooldown
            ? V113StatsPackets.EnableActions()
            : result.CooldownStartedSeconds > 0
                ? V113SkillPackets.SkillCooldown(request.SkillId, result.CooldownStartedSeconds)
                : null;

        // P078：對照 Java SpecialMove 開頭（死亡 → enableActions）與 MapleStatEffect.applyTo（成功套用後一律
        // updatePlayerStats(HP[, MP 有變動才帶], itemReaction=true)，讓客戶端 HP/MP 同步並解鎖動作）。
        var statsPacket = result.Status switch
        {
            SkillCastStatus.Dead => V113StatsPackets.EnableActions(),
            SkillCastStatus.Success => V113StatsPackets.UpdateStats(BuildCastStatUpdates(player, mpBefore), itemReaction: true),
            _ => null,
        };

        // P086：時間置換清掉的冷卻逐一送 skillCooldown(id, 0)。
        var resetPackets = result.ResetCooldownSkillIds?
            .Select(static id => V113SkillPackets.SkillCooldown(id, 0))
            .ToArray();

        return new V113SkillHandleResult(request.SkillId, packet, result, null, cooldownPacket, statsPacket, request, resetPackets);
    }

    private static IEnumerable<PlayerStatUpdate> BuildCastStatUpdates(Player player, short mpBefore)
    {
        if (player.Mp != mpBefore)
        {
            yield return new PlayerStatUpdate(PlayerStatKind.Mp, player.Mp);
        }

        yield return new PlayerStatUpdate(PlayerStatKind.Hp, player.Hp);
    }

    /// <summary>
    /// 攻擊技能冷卻（對照 Java 三個攻擊 handler 的冷卻區塊）：冷卻中回 <c>Blocked=true</c> +
    /// <c>EnableActions</c>（整次攻擊丟棄）；登記冷卻回 <c>COOLDOWN</c> 封包；其他情況兩者皆空。
    /// </summary>
    public static (bool Blocked, byte[]? Packet) HandleAttackCooldown(
        Player player,
        int skillId,
        SkillService skillService,
        DateTimeOffset now)
    {
        var result = skillService.TryStartAttackCooldown(player, skillId, now);
        return result.Status switch
        {
            AttackCooldownStatus.OnCooldown => (true, V113StatsPackets.EnableActions()),
            AttackCooldownStatus.Started => (false, V113SkillPackets.SkillCooldown(skillId, result.Seconds)),
            _ => (false, null),
        };
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
