using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal sealed record V113SpawnGuildInfo(
    string Name,
    short LogoBackground,
    byte LogoBackgroundColor,
    short Logo,
    byte LogoColor);

/// <summary>CHANGE_MAP (c2s 0x1E) 解析結果。Mode：1=死亡 2=一般 portal；TargetId：一般腳走 portal = -1。</summary>
internal readonly record struct V113ChangeMapRequest(byte Mode, int TargetId, string PortalName);

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
    public static byte[] SpawnPlayer(Character chr, short x, short y, byte stance, short foothold, V113SpawnGuildInfo? guild = null)
    {
        var w = new PacketWriter(512);
        w.WriteShort(SpawnPlayerOp);
        w.WriteInt(chr.Id);
        w.WriteByte(chr.Level);
        w.WriteMapleString(chr.Name);
        if (guild is null)
        {
            w.WriteMapleString(string.Empty);
            w.WriteZeroBytes(6);
        }
        else
        {
            w.WriteMapleString(guild.Name);
            w.WriteShort(guild.LogoBackground);
            w.WriteByte(guild.LogoBackgroundColor);
            w.WriteShort(guild.Logo);
            w.WriteByte(guild.LogoColor);
        }

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

    /// <summary>
    /// CHATTEXT (0x9B) — 地圖聊天泡泡。對照 Java getChatText(cidfrom,text,whiteBG,show)。
    /// 格式：[opcode][int charId][byte whiteBG][maple string text][byte show]。
    /// </summary>
    public static byte[] ChatText(int charId, string text, byte show, bool whiteBg = false)
    {
        var w = new PacketWriter(text.Length + 16);
        w.WriteShort(V113ChannelSendOp.ChatText);
        w.WriteInt(charId);
        w.WriteByte(whiteBg ? (byte)1 : (byte)0);
        w.WriteMapleString(text);
        w.WriteByte(show);
        return w.ToArray();
    }

    /// <summary>
    /// FACIAL_EXPRESSION (0xB9) — 玩家表情廣播。對照 Java facialExpression(from, expression)。
    /// 格式：[opcode][int charId][int expression]。
    /// </summary>
    public static byte[] FacialExpression(int charId, int expression)
    {
        var w = new PacketWriter(10);
        w.WriteShort(V113ChannelSendOp.FacialExpression);
        w.WriteInt(charId);
        w.WriteInt(expression);
        return w.ToArray();
    }

    /// <summary>
    /// SHOW_CHAIR (0xBD) — 同地圖其他玩家看到角色坐上或離開椅子。對照 Java showChair。
    /// itemId = 0 表示清除椅子外觀。
    /// </summary>
    public static byte[] ShowChair(int charId, int itemId)
    {
        var w = new PacketWriter(10);
        w.WriteShort(V113ChannelSendOp.ShowChair);
        w.WriteInt(charId);
        w.WriteInt(itemId);
        return w.ToArray();
    }

    /// <summary>
    /// CANCEL_CHAIR (0xC6) — 回給本人控制椅子狀態。對照 Java cancelChair。
    /// id = -1 時 layout 為 [opcode][0]；否則 [opcode][1][short id]。
    /// </summary>
    public static byte[] CancelChair(short id)
    {
        var w = new PacketWriter(id == -1 ? 3 : 5);
        w.WriteShort(V113ChannelSendOp.CancelChair);
        if (id == -1)
        {
            w.WriteByte(0);
        }
        else
        {
            w.WriteByte(1);
            w.WriteShort(id);
        }
        return w.ToArray();
    }

    /// <summary>
    /// SHOW_ITEM_EFFECT (0xBA) — 玩家頭頂道具效果。對照 Java itemEffect。
    /// itemId = 0 可用於清除目前道具效果。
    /// </summary>
    public static byte[] ItemEffect(int charId, int itemId)
    {
        var w = new PacketWriter(10);
        w.WriteShort(V113ChannelSendOp.ShowItemEffect);
        w.WriteInt(charId);
        w.WriteInt(itemId);
        return w.ToArray();
    }

    /// <summary>
    /// SPAWN_NPC (0xF9) — 讓進場客戶端看到地圖 NPC。對照 Java spawnNPC。
    /// 布局：[opcode][int objectId][int npcId][short x][short cy][byte dir][short fh][short rx0][short rx1][byte show]。
    /// dir = (f==1 ? 0 : 1)（Java 慣例）。
    /// </summary>
    public static byte[] SpawnNpc(Npc npc, bool show = true)
    {
        var w = new PacketWriter(24);
        w.WriteShort(V113ChannelSendOp.SpawnNpc);
        WriteNpcBody(w, npc);
        w.WriteByte(show ? (byte)1 : (byte)0);
        return w.ToArray();
    }

    /// <summary>
    /// SPAWN_NPC_REQUEST_CONTROLLER (0xFB)，控制旗標=1 — 指派客戶端為該 NPC 的控制者。
    /// 對照 Java spawnNPCRequestController。布局同 SpawnNpc，但前綴 [byte 1] 控制旗標、尾端 [byte miniMap]。
    /// </summary>
    public static byte[] SpawnNpcRequestController(Npc npc, bool miniMap = true)
    {
        var w = new PacketWriter(26);
        w.WriteShort(V113ChannelSendOp.SpawnNpcRequestController);
        w.WriteByte(1);   // 1 = 取得控制權（0 = 移除控制權，見 RemoveNpcController）
        WriteNpcBody(w, npc);
        w.WriteByte(miniMap ? (byte)1 : (byte)0);
        return w.ToArray();
    }

    /// <summary>REMOVE_NPC (0xFA)。</summary>
    public static byte[] RemoveNpc(int objectId)
    {
        var w = new PacketWriter(6);
        w.WriteShort(V113ChannelSendOp.RemoveNpc);
        w.WriteInt(objectId);
        return w.ToArray();
    }

    /// <summary>
    /// 解析 CHANGE_MAP (c2s 0x1E) body（2-byte opcode 已讀掉）。對照 Java PlayerHandler.ChangeMap。
    /// 結構：byte(1=死亡 2=一般 portal) + int targetId(一般腳走 foot portal = -1) +
    ///       MapleAsciiString portalName + skip(1) + short wheel(原地復活輪，MVP 不處理)。
    /// 尾端精確消費（team consult：避免影響死亡分支判斷/除錯）。
    /// </summary>
    public static V113ChangeMapRequest ParseChangeMap(PacketReader r)
    {
        byte mode = r.ReadByte();
        int targetId = r.ReadInt();
        string portalName = r.ReadMapleString();
        if (r.Remaining >= 1) r.ReadByte();    // skip(1)
        if (r.Remaining >= 2) r.ReadShort();   // wheel
        return new V113ChangeMapRequest(mode, targetId, portalName);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>SpawnNpc 與 controller 共用的 NPC 本體（id + 位置 + 移動範圍）。</summary>
    private static void WriteNpcBody(PacketWriter w, Npc npc)
    {
        var d = npc.Definition;
        w.WriteInt(npc.ObjectId);
        w.WriteInt(d.NpcId);
        w.WriteShort((short)d.X);
        w.WriteShort((short)d.Cy);
        w.WriteByte(d.F == 1 ? (byte)0 : (byte)1);
        w.WriteShort((short)d.Fh);
        w.WriteShort((short)d.Rx0);
        w.WriteShort((short)d.Rx1);
    }

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
