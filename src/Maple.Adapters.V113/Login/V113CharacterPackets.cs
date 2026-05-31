using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Login;

/// <summary>
/// v113 角色相關封包序列化（對照舊 PacketHelper.addCharStats/addCharLook + LoginPacket.addCharEntry/addNewCharEntry）。
/// 所有格式均從舊 Java 神諭逐 byte 對照。
/// </summary>
internal static class V113CharacterPackets
{
    // ── 公開組裝入口 ───────────────────────────────────────────────────────────

    /// <summary>CHAR_NAME_RESPONSE (0x05)：0=名稱可用, 1=已使用。</summary>
    public static byte[] CharNameResponse(string name, bool nameUsed)
        => new PacketWriter(32)
            .WriteShort(V113SendOp.CharNameResponse)
            .WriteMapleString(name)
            .WriteByte(nameUsed ? 1 : 0)
            .ToArray();

    /// <summary>ADD_NEW_CHAR_ENTRY (0x06)：建角成功/失敗＋角色資料。</summary>
    public static byte[] AddNewCharEntry(Character chr, bool success)
    {
        var w = new PacketWriter(256)
            .WriteShort(V113SendOp.AddNewCharEntry)
            .WriteByte(success ? 0 : 1);   // 0=成功, 1=失敗
        WriteCharEntry(w, chr, ranking: false);
        return w.ToArray();
    }

    /// <summary>單筆 CharEntry（CharStats + CharLook + 附加欄位），直接寫進 PacketWriter。</summary>
    public static void WriteCharEntry(PacketWriter w, Character chr, bool ranking)
    {
        WriteCharStats(w, chr);
        WriteCharLook(w, chr, megaphone: false);
        w.WriteByte(0);                     // 未知（Java: mplew.write(0)）
        w.WriteByte(ranking ? 1 : 0);
        if (ranking)
        {
            w.WriteInt(0).WriteInt(0).WriteInt(0).WriteInt(0);
        }
    }

    // ── 內部序列化 ─────────────────────────────────────────────────────────────

    /// <summary>addCharStats（對照 PacketHelper.addCharStats）。</summary>
    private static void WriteCharStats(PacketWriter w, Character chr)
    {
        w.WriteInt(chr.Id)                              // character id
         .WriteFixedAsciiString(chr.Name, 15)          // name padded to 15 bytes
         .WriteByte(chr.Gender)
         .WriteByte(chr.SkinColor)
         .WriteInt(chr.Face)
         .WriteInt(chr.Hair)
         .WriteZeroBytes(24)                            // pet slots (24 bytes)
         .WriteByte(chr.Level)
         .WriteShort(chr.Job);

        // connectData: str dex int luk hp maxhp mp maxmp（各 short）
        var s = chr.Stats;
        w.WriteShort(s.Str)
         .WriteShort(s.Dex)
         .WriteShort(s.Int)
         .WriteShort(s.Luk)
         .WriteShort(s.Hp)
         .WriteShort(s.MaxHp)
         .WriteShort(s.Mp)
         .WriteShort(s.MaxMp);

        w.WriteShort(chr.RemainingAp)
         .WriteShort(chr.RemainingSp)
         .WriteInt(chr.Exp)
         .WriteShort(chr.Fame)
         .WriteInt(chr.GachExp)
         .WriteLong(0)                                  // Java: writeLong(0)
         .WriteInt(chr.MapId)
         .WriteByte(chr.SpawnPoint)
         .WriteZeroBytes(25)                            // 台版特有
         .WriteByte(1).WriteByte(1).WriteByte(1).WriteByte(1).WriteByte(1);
    }

    /// <summary>addCharLook（對照 PacketHelper.addCharLook）。</summary>
    private static void WriteCharLook(PacketWriter w, Character chr, bool megaphone)
    {
        w.WriteByte(chr.Gender)
         .WriteByte(chr.SkinColor)
         .WriteInt(chr.Face)
         .WriteByte(megaphone ? 1 : 0)  // mega=false for charlist
         .WriteInt(chr.Hair);

        // visible equipment（position < 100, not masked）
        // 簡化：只寫全部 equip，無 masking（新角色不會有重疊槽）
        foreach (var e in chr.Equips)
        {
            byte slot = (byte)(e.Position * -1);
            if (slot is > 0 and < 100)
                w.WriteByte(slot).WriteInt(e.ItemId);
        }
        w.WriteByte(0xFF);  // end of visible

        // masked equipment（暫無）
        w.WriteByte(0xFF);  // end of masked

        // cash weapon
        w.WriteInt(0)       // cash weapon item id (none)
         .WriteInt(0)
         .WriteLong(0);
    }
}
