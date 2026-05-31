using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// v113 地圖相關封包：SPAWN_PLAYER / REMOVE_PLAYER_FROM_MAP / MOVE_PLAYER 廣播。
/// 封包格式來自舊 Java MaplePacketCreator.spawnPlayerMapobject / removePlayerFromMap / movePlayer。
/// </summary>
internal static class V113MapPackets
{
    // 0x99 = SPAWN_PLAYER（讓其他玩家看到進入地圖的玩家）
    private const short SpawnPlayerOp = 0x99;
    // 0x9A = REMOVE_PLAYER_FROM_MAP（讓其他玩家移除離開地圖的玩家）
    private const short RemovePlayerOp = unchecked((short)0x9A);
    // 0xB1 = MOVE_PLAYER 廣播
    private const short MovePlayerOp = unchecked((short)0xB1);

    /// <summary>
    /// SPAWN_PLAYER 封包（最小版本，無 buff/mount/ring）。
    /// DoD：客戶端能在地圖上看到其他玩家的角色外觀與位置。
    /// </summary>
    public static byte[] SpawnPlayer(Character chr, short x, short y, byte stance, short foothold)
    {
        var w = new PacketWriter(512);
        w.WriteShort(SpawnPlayerOp);
        w.WriteInt(chr.Id);
        w.WriteByte(chr.Level);
        w.WriteMapleString(chr.Name);
        // Guild: empty
        w.WriteMapleString(string.Empty);
        w.WriteShort(0);   // guild logo BG
        w.WriteByte(0);    // guild logo BG color
        w.WriteShort(0);   // guild logo
        w.WriteByte(0);    // guild logo color

        // Buff masks
        w.WriteLong(0x00FFFC0000000000L);  // fbuffmask
        w.WriteLong(0L);                    // buffmask (no buffs)

        // 7 CHAR_MAGIC_SPAWN blocks (no buff effects; tick = 0)
        int tick = 0;
        WriteCharMagicShortBlock(w, tick);   // energy charge
        WriteCharMagicShortBlock(w, tick);   // dash speed
        WriteCharMagicShortBlock(w, tick);   // dash jump
        WriteCharMagicShortBlock(w, tick);   // monster riding
        WriteCharMagicLongBlock(w, tick);    // speed infusion
        // homing beacon
        w.WriteInt(1); w.WriteLong(0L); w.WriteByte(0); w.WriteShort(0); w.WriteByte(1); w.WriteInt(tick);
        // unknown
        w.WriteInt(0); w.WriteLong(0L); w.WriteByte(1); w.WriteInt(tick);
        // TMS extra
        w.WriteShort(0); w.WriteLong(0L); w.WriteByte(1); w.WriteInt(tick);

        w.WriteShort(chr.Job);
        AddCharLook(w, chr);
        w.WriteInt(0);    // cash chair count
        w.WriteInt(0);    // item effect
        w.WriteInt(0);    // TMS extra
        w.WriteInt(0);    // TMS extra
        w.WriteInt(-1);   // TMS extra (-1)
        w.WriteInt(0);    // chair (0 = none)
        w.WriteShort(x);
        w.WriteShort(y);
        w.WriteByte(stance);
        w.WriteShort(foothold);
        w.WriteByte(0);
        w.WriteInt(1);    // mount level
        w.WriteInt(0);    // mount exp
        w.WriteInt(0);    // mount fatigue
        // addAnnounceBox
        w.WriteByte(0); w.WriteInt(0); w.WriteShort(0); w.WriteShort(0);
        w.WriteByte(0);   // chalkboard = none
        // rings × 2
        w.WriteShort(0); w.WriteShort(0);
        // marriage ring look
        w.WriteByte(0);
        w.WriteShort(0);

        return w.ToArray();
    }

    /// <summary>REMOVE_PLAYER_FROM_MAP (0x9A)。</summary>
    public static byte[] RemovePlayer(int charId)
    {
        var w = new PacketWriter(6);
        w.WriteShort(RemovePlayerOp);
        w.WriteInt(charId);
        return w.ToArray();
    }

    /// <summary>
    /// MOVE_PLAYER 廣播 (0xB1)。
    /// rawMovement = 客戶端封包扣掉前 35 bytes（2 opcode + 33 unknown header）後的 bytes。
    /// </summary>
    public static byte[] MovePlayerBroadcast(int charId, ReadOnlySpan<byte> rawMovement)
    {
        var w = new PacketWriter(rawMovement.Length + 10);
        w.WriteShort(MovePlayerOp);
        w.WriteInt(charId);
        w.WriteInt(0);
        w.WriteBytes(rawMovement);
        return w.ToArray();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void WriteCharMagicShortBlock(PacketWriter w, int tick)
    {
        w.WriteShort(0); w.WriteLong(0L); w.WriteByte(1); w.WriteInt(tick);
    }

    private static void WriteCharMagicLongBlock(PacketWriter w, int tick)
    {
        w.WriteLong(0L); w.WriteByte(1); w.WriteInt(tick);
    }

    private static void AddCharLook(PacketWriter w, Character chr)
    {
        w.WriteByte(chr.Gender);
        w.WriteByte(chr.SkinColor);
        w.WriteInt(chr.Face);
        w.WriteByte(1);
        w.WriteInt(chr.Hair);
        foreach (var eq in chr.Equips.Where(e => e.Position < 0 && e.Position > -100))
        {
            w.WriteByte((byte)(-eq.Position));
            w.WriteInt(eq.ItemId);
        }
        w.WriteByte(0xFF);
        w.WriteByte(0xFF);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteLong(0L);
    }
}
