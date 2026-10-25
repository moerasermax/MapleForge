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
    IReadOnlyList<byte[]>? CooldownResetPackets = null,
    byte[]? MulungEnergyPacket = null);

/// <summary>v113 技能/buff 封包。對照 Java PlayerHandler.SpecialMove/CancelBuffHandler 與 MaplePacketCreator.giveBuff/cancelBuff。</summary>
internal static class V113SkillPackets
{
    public const short SpecialMoveRecvOp = 0x55;
    public const short SessionValueOp = 0x54; // P108：send.properties SESSION_VALUE
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


    /// <summary>
    /// P159：Java <c>giveHoming(skillid, mobid)</c>：<c>[GIVE_BUFF][mask(HOMING_BEACON)][short 0][int 1][long 技能][byte 0][int 怪物 oid][short 0]</c>
    /// （unverified）。
    /// </summary>
    public static byte[] GiveHoming(int skillId, int mobObjectId)
    {
        var w = new PacketWriter(40);
        w.WriteShort(GiveBuffOp);
        WriteBuffMask(w, [MapleBuffStat.HOMING_BEACON]);
        w.WriteShort(0);
        w.WriteInt(1);
        w.WriteLong(skillId);
        w.WriteByte(0);
        w.WriteInt(mobObjectId);
        w.WriteShort(0);
        return w.ToArray();
    }

    /// <summary>
    /// P159：Java <c>cancelHoming()</c>：<c>[CANCEL_BUFF][long HOMING_BEACON.getValue()][long 0]</c>。
    /// Java 直接寫 <c>getValue()</c>（1 &lt;&lt; 16）到第一個 long，而非 writeBuffState 的位置（mask[1]）——照抄，unverified。
    /// </summary>
    public static byte[] CancelHoming()
    {
        var w = new PacketWriter(18);
        w.WriteShort(CancelBuffOp);
        w.WriteLong(1L << ((int)MapleBuffStat.HOMING_BEACON % 32));
        w.WriteLong(0);
        return w.ToArray();
    }

    /// <summary>P158：Java <c>skillCancel</c>：CANCEL_SKILL_EFFECT [int charId][int skillId]（unverified：未經真機擷取）。</summary>
    public static byte[] CancelSkillEffect(int characterId, int skillId)
    {
        var w = new PacketWriter();
        w.WriteShort(RemoteCancelSkillEffectOp);
        w.WriteInt(characterId);
        w.WriteInt(skillId);
        return w.ToArray();
    }

    /// <summary>
    /// 對照 Java <c>MaplePacketCreator.MulungEnergy</c> → <c>sendString(1, "energy", value)</c>：
    /// SESSION_VALUE 兩個 MapleAsciiString（unverified：未經真機擷取）。
    /// </summary>
    public static byte[] MulungEnergy(int energy)
    {
        var w = new PacketWriter();
        w.WriteShort(SessionValueOp);
        w.WriteMapleString("energy");
        w.WriteMapleString(energy.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return w.ToArray();
    }
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

    /// <summary>
    /// P147：對照 Java <c>MaplePacketCreator.givePirate(statups, duration秒, skillid)</c>（衝刺／極速領域／龍捲風）：
    /// <c>[GIVE_BUFF][mask][short 0]</c>，每個 stat <c>[int 值][long 技能][極速領域 6／其他 1 個 0][short 秒]</c>，
    /// 結尾 <c>[short 極速領域 600／其他 0]</c>，非極速領域再加 <c>[byte 1]</c>。
    /// </summary>
    public static byte[] GivePirate(IReadOnlyList<BuffStatValue> statups, int durationSeconds, int skillId)
    {
        var infusion = IsSpeedInfusion(skillId);
        var w = new PacketWriter(32 + statups.Count * 20);
        w.WriteShort(GiveBuffOp);
        WriteBuffMask(w, statups.Select(static s => s.Stat));
        w.WriteShort(0);
        foreach (var statup in statups)
        {
            w.WriteInt(statup.Value);
            w.WriteLong(skillId);
            w.WriteZeroBytes(infusion ? 6 : 1);
            w.WriteShort((short)durationSeconds);
        }

        w.WriteShort((short)(infusion ? 600 : 0));
        if (!infusion)
        {
            w.WriteByte(1);
        }

        return w.ToArray();
    }

    /// <summary>
    /// P147：對照 Java <c>giveForeignPirate(statups, duration秒, cid, skillid)</c>：<c>[GIVE_FOREIGN_BUFF][int cid][mask][short 0]</c>，
    /// 每個 stat <c>[int 值][long 技能][極速領域 7／其他 1 個 0][short 秒]</c>，結尾 <c>[short 極速領域 600／其他 0]</c>。
    /// </summary>
    public static byte[] GiveForeignPirate(IReadOnlyList<BuffStatValue> statups, int durationSeconds, int characterId, int skillId)
    {
        var infusion = IsSpeedInfusion(skillId);
        var w = new PacketWriter(36 + statups.Count * 20);
        w.WriteShort(GiveForeignBuffOp);
        w.WriteInt(characterId);
        WriteBuffMask(w, statups.Select(static s => s.Stat));
        w.WriteShort(0);
        foreach (var statup in statups)
        {
            w.WriteInt(statup.Value);
            w.WriteLong(skillId);
            w.WriteZeroBytes(infusion ? 7 : 1);
            w.WriteShort((short)durationSeconds);
        }

        w.WriteShort((short)(infusion ? 600 : 0));
        return w.ToArray();
    }

    private static bool IsSpeedInfusion(int skillId) => skillId is 5121009 or 15111005;

    private static readonly BuffStatValue[] MountStatups = [new BuffStatValue(MapleBuffStat.MONSTER_RIDING, 0)];

    /// <summary>
    /// P157：對照 Java <c>giveMount(buffid = 騎寵道具, skillid, [MONSTER_RIDING 0])</c>：
    /// <c>[GIVE_BUFF][mask][short 0][int 道具][int 技能][int 0][short 0][byte 0][byte 2]</c>。
    /// </summary>
    public static byte[] GiveMount(int mountItemId, int skillId)
    {
        var w = new PacketWriter(40);
        w.WriteShort(GiveBuffOp);
        WriteBuffMask(w, MountStatups.Select(static s => s.Stat));
        w.WriteShort(0);
        w.WriteInt(mountItemId);
        w.WriteInt(skillId);
        w.WriteInt(0);
        w.WriteShort(0);
        w.WriteByte(0);
        w.WriteByte(2);
        return w.ToArray();
    }

    /// <summary>
    /// P157：對照 Java <c>showMonsterRiding(cid, [MONSTER_RIDING 0], itemId, skillId)</c>：
    /// <c>[GIVE_FOREIGN_BUFF][int cid][mask][short 0][int 道具][int 技能][int 0][short 0][byte 0][byte 0]</c>。
    /// </summary>
    public static byte[] ShowMonsterRiding(int characterId, int mountItemId, int skillId)
    {
        var w = new PacketWriter(44);
        w.WriteShort(GiveForeignBuffOp);
        w.WriteInt(characterId);
        WriteBuffMask(w, MountStatups.Select(static s => s.Stat));
        w.WriteShort(0);
        w.WriteInt(mountItemId);
        w.WriteInt(skillId);
        w.WriteInt(0);
        w.WriteShort(0);
        w.WriteByte(0);
        w.WriteByte(0);
        return w.ToArray();
    }

    public static byte[] CancelBuff(IReadOnlyList<MapleBuffStat> stats)
    {
        // P159：Java cancelPlayerBuffs：含 HOMING_BEACON 時只送 cancelHoming（不送一般 cancelBuff）。
        if (stats.Contains(MapleBuffStat.HOMING_BEACON))
        {
            return CancelHoming();
        }

        var w = new PacketWriter(24);
        w.WriteShort(CancelBuffOp);
        WriteBuffMask(w, stats);
        w.WriteByte(3);
        return w.ToArray();
    }

    /// <summary>P139：對照 Java <c>MaplePacketCreator.cancelForeignBuff(cid, statups)</c>：<c>[0xC1][int cid][buff mask]</c>。</summary>
    public static byte[] CancelForeignBuff(int characterId, IReadOnlyList<MapleBuffStat> stats)
    {
        var w = new PacketWriter(24);
        w.WriteShort(CancelForeignBuffOp);
        w.WriteInt(characterId);
        WriteBuffMask(w, stats);
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
            ? result.Mount is { } mount
                ? V113SkillPackets.GiveMount(mount.SelfMountItemId, request.SkillId)
                : BuildGiveBuff(request.SkillId, result.AppliedBuff, result.Effect)
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

        var mulungPacket = result.MulungEnergyReset ? V113SkillPackets.MulungEnergy(player.MulungEnergy) : null;

        return new V113SkillHandleResult(request.SkillId, packet, result, null, cooldownPacket, statsPacket, request, resetPackets, mulungPacket);
    }

    /// <summary>P147：海盜衝刺類用 givePirate（秒數 = localDuration / 1000），其餘一般 giveBuff。</summary>
    internal static byte[] BuildGiveBuff(int skillId, PlayerBuffChange buff, MapleStatEffect effect)
        => SkillService.IsPirateBuff(effect)
            ? V113SkillPackets.GivePirate(buff.Stats, buff.DurationMilliseconds / 1000, skillId)
            : V113SkillPackets.GiveBuff(skillId, buff.DurationMilliseconds, buff.Stats, effect);

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
