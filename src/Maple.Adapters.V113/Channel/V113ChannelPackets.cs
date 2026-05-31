using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// v113 Channel 封包建構器。
/// 封包格式來自舊 Java MaplePacketCreator.getCharInfo / PacketHelper.addCharacterInfo。
/// </summary>
internal static class V113ChannelPackets
{
    /// <summary>
    /// SET_FIELD (0x7B) — 初次進入頻道，傳送完整角色資料。
    /// 結構：opcode + channel + 1 + 1 + 0 + CRand(4 int) + addCharacterInfo + time
    /// </summary>
    public static byte[] SetField(Character chr, int channelIndex)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.SetField);
        w.WriteInt(channelIndex);       // channel - 1
        w.WriteByte(1);                 // isFirstLogin flag
        w.WriteByte(1);
        w.WriteShort(0);

        // CRand state (4 × int, anti-cheat RNG seed) — zeros ok for private server
        w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0);

        AddCharacterInfo(w, chr);

        w.WriteLong(GetTime());
        return w.ToArray();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void AddCharacterInfo(PacketWriter w, Character chr)
    {
        w.WriteLong(-1L);               // placeholder / version marker
        w.WriteByte(0);

        AddCharStats(w, chr);

        w.WriteByte(20);                // buddy list capacity
        w.WriteByte(0);                 // no blessOfFairy

        w.WriteLong(GetTime());

        AddInventoryInfo(w, chr);
        AddSkillInfo(w);
        AddCoolDownInfo(w);
        AddQuestInfo(w);
        AddRingInfo(w);
        AddRocksInfo(w);
        AddMonsterBookInfo(w);

        w.WriteShort(0);                // quest info packet count
        w.WriteShort(0);
        w.WriteShort(0);
        w.WriteShort(0);
    }

    private static void AddCharStats(PacketWriter w, Character chr)
    {
        w.WriteInt(chr.Id);
        w.WriteFixedAsciiString(chr.Name, 15);
        w.WriteByte(chr.Gender);
        w.WriteByte(chr.SkinColor);
        w.WriteInt(chr.Face);
        w.WriteInt(chr.Hair);
        w.WriteZeroBytes(24);               // pet OIDs × 3 (int × 3 × 2 arrays) = 24 bytes
        w.WriteByte(chr.Level);
        w.WriteShort(chr.Job);

        // connectData (PlayerStats)
        w.WriteShort(chr.Stats.Str);
        w.WriteShort(chr.Stats.Dex);
        w.WriteShort(chr.Stats.Int);
        w.WriteShort(chr.Stats.Luk);
        w.WriteShort(chr.Stats.Hp);
        w.WriteShort(chr.Stats.MaxHp);
        w.WriteShort(chr.Stats.Mp);
        w.WriteShort(chr.Stats.MaxMp);

        w.WriteShort(chr.RemainingAp);
        w.WriteShort(chr.RemainingSp);
        w.WriteInt(chr.Exp);
        w.WriteShort(chr.Fame);
        w.WriteInt(chr.GachExp);        // gachapon exp
        w.WriteLong(0);                 // unknown (8 bytes)
        w.WriteInt(chr.MapId);
        w.WriteByte(chr.SpawnPoint);
        w.WriteZeroBytes(25);               // TMS extra padding
        w.WriteByte(1); w.WriteByte(1); w.WriteByte(1); w.WriteByte(1); w.WriteByte(1);
    }

    private static void AddInventoryInfo(PacketWriter w, Character chr)
    {
        w.WriteInt(0);                  // mesos
        w.WriteInt(chr.Id);             // char id (repeated for some reason)
        w.WriteInt(0);                  // beans
        w.WriteInt(0);

        // inventory slot limits
        w.WriteByte(24);   // equip
        w.WriteByte(24);   // use
        w.WriteByte(24);   // setup
        w.WriteByte(24);   // etc
        w.WriteByte(48);   // cash

        w.WriteLong(GetTime(-2L));      // -2 = marker timestamp

        // Equipped items (position < 0 and > -100 = visible equip slots)
        foreach (var eq in chr.Equips.Where(e => e.Position < 0 && e.Position > -100))
        {
            AddEquipItemInfo(w, eq, false);
        }
        w.WriteByte(0);

        // Equipped NX items (position <= -100 and > -1000)
        foreach (var eq in chr.Equips.Where(e => e.Position <= -100 && e.Position > -1000))
        {
            AddEquipItemInfo(w, eq, false);
        }
        w.WriteByte(0);

        // Equip bag, Use, Setup, Etc, Cash (all empty)
        w.WriteByte(0);
        w.WriteByte(0);
        w.WriteByte(0);
        w.WriteByte(0);
        w.WriteByte(0);
        w.WriteByte(0);
    }

    private static void AddEquipItemInfo(PacketWriter w, EquipEntry eq, bool zeroPosition)
    {
        // Item info: type byte + position + itemId + owner + flag + ...
        w.WriteByte(1);                 // item type = equip
        if (!zeroPosition)
        {
            w.WriteShort(eq.Position);
        }
        w.WriteInt(eq.ItemId);
        w.WriteByte(0);                 // unique flag (0 = normal)
        w.WriteLong(-1L);               // expiration time = never
        // equip stats (all zero for minimal)
        w.WriteByte(0);                 // upgrade slots
        w.WriteByte(0);                 // level
        w.WriteShort(0); w.WriteShort(0); w.WriteShort(0); w.WriteShort(0);  // str/dex/int/luk
        w.WriteShort(0); w.WriteShort(0); w.WriteShort(0); w.WriteShort(0);  // hp/mp/watk/matk
        w.WriteShort(0); w.WriteShort(0); w.WriteShort(0);  // wacc/macc/avoid
        w.WriteShort(0); w.WriteShort(0); w.WriteShort(0);  // hands/speed/jump
        w.WriteInt(0);                  // owner length
        w.WriteShort(0);                // flag
        w.WriteByte(0);                 // item level
        w.WriteShort(0);                // item exp
        w.WriteInt(-1);                 // vicious hammer = -1
        w.WriteLong(0);                 // ring id (0 = no ring)
    }

    private static void AddSkillInfo(PacketWriter w) => w.WriteShort(0);

    private static void AddCoolDownInfo(PacketWriter w) => w.WriteShort(0);

    private static void AddQuestInfo(PacketWriter w)
    {
        w.WriteShort(0);   // active quests
        w.WriteShort(0);   // completed quests
    }

    private static void AddRingInfo(PacketWriter w)
    {
        w.WriteShort(0);
        w.WriteShort(0);
        w.WriteShort(0);
    }

    private static void AddRocksInfo(PacketWriter w)
    {
        for (var i = 0; i < 10; i++) w.WriteInt(0);   // regular rocks
        for (var i = 0; i < 5; i++) w.WriteInt(0);    // VIP rocks
    }

    private static void AddMonsterBookInfo(PacketWriter w)
    {
        w.WriteInt(0);      // cover card
        w.WriteByte(0);
        w.WriteShort(0);    // entry count
    }

    private static long GetTime(long offset = 0)
    {
        // MapleStory FILETIME: milliseconds since 1970-01-01 * 10000 + Korean epoch offset
        const long KoreanEpochOffset = 116444736000000000L;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (offset < 0)
        {
            return KoreanEpochOffset + offset;
        }

        return KoreanEpochOffset + (now * 10000);
    }
}
