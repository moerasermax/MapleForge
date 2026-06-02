using Maple.Core.Characters;
using Maple.Core.Inventory;
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

        // CRand state (3 × int, anti-cheat RNG seed) — 對齊 Java PlayerRandomStream.connectData(3 int)
        // 先前誤寫 4 個 → 整段 addCharacterInfo 位移 → 客戶端讀位移後誤判長度欄 → EOF(error 38)
        w.WriteInt(0); w.WriteInt(0); w.WriteInt(0);

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
        w.WriteInt(chr.Meso);           // mesos
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

        // 五袋真實道具（EQUIP/USE/SETUP/ETC/CASH，各以 0 結束）。對照 Java addInventoryInfo。
        foreach (var type in new[] { InventoryType.Equip, InventoryType.Use, InventoryType.Setup, InventoryType.Etc, InventoryType.Cash })
        {
            foreach (var rec in chr.Items.Where(i => i.Type == (byte)type && i.Slot > 0).OrderBy(i => i.Slot))
                AddBagItemInfo(w, rec);
            w.WriteByte(0);
        }
    }

    /// <summary>
    /// 背包格內單一道具（正 slot）。對照 Java PacketHelper.addItemInfo：
    /// 裝備分支 = position/type1/itemId/hasUid/expire/upgrade/level/15short/owner/flag/incSkill/itemLevel/itemExp/uid/time/-1；
    /// 堆疊分支 = position/type2/itemId/hasUid/expire/quantity/owner/flag。MVP-0 裝備渲染零屬性(RawEquip)。
    /// </summary>
    private static void AddBagItemInfo(PacketWriter w, ItemRecord r)
    {
        w.WriteByte((byte)r.Slot);              // 位置（背包格＝正數）

        if (r.IsEquip)
        {
            w.WriteByte(1);                     // type = equip
            w.WriteInt(r.ItemId);
            w.WriteByte(0);                     // hasUniqueId = 0
            w.WriteLong(GetTime(r.Expiration));
            w.WriteByte(r.UpgradeSlots);
            w.WriteByte(r.Level);
            w.WriteShort(r.Str); w.WriteShort(r.Dex); w.WriteShort(r.Int); w.WriteShort(r.Luk);
            w.WriteShort(r.Hp); w.WriteShort(r.Mp); w.WriteShort(r.Watk); w.WriteShort(r.Matk);
            w.WriteShort(r.Wdef); w.WriteShort(r.Mdef); w.WriteShort(r.Acc); w.WriteShort(r.Avoid);
            w.WriteShort(r.Hands); w.WriteShort(r.Speed); w.WriteShort(r.Jump);
            w.WriteMapleString(r.Owner);
            w.WriteShort(r.Flag);
            w.WriteByte(0);                     // incSkill
            w.WriteByte(r.ItemLevel);
            w.WriteInt(r.ItemExp);
            w.WriteLong(r.UniqueId);
            w.WriteLong(GetTime(-2));
            w.WriteInt(-1);
        }
        else
        {
            w.WriteByte(2);                     // type = normal/stackable
            w.WriteInt(r.ItemId);
            w.WriteByte(0);                     // hasUniqueId = 0
            w.WriteLong(GetTime(r.Expiration));
            w.WriteShort(r.Quantity);
            w.WriteMapleString(r.Owner);
            w.WriteShort(r.Flag);
        }
    }

    private static void AddEquipItemInfo(PacketWriter w, EquipEntry eq, bool zeroPosition)
    {
        // 逐欄對照 Java PacketHelper.addItemInfo (equip 分支, 非寵物/無 uniqueId)
        if (!zeroPosition)
        {
            short pos = eq.Position;
            if (pos < 0) pos = (short)(-pos);
            w.WriteByte((byte)(pos > 100 ? pos - 100 : pos)); // 位置: 1 byte, 絕對值, >100→-100 (先寫位置再 type!)
        }
        w.WriteByte(1);                 // type = equip (非寵物=3)
        w.WriteInt(eq.ItemId);
        w.WriteByte(0);                 // hasUniqueId = 0 (一般裝備)
        // hasUniqueId=0 → 不寫 uniqueId long
        w.WriteLong(GetTime(-1));       // addExpirationTime = writeLong(getTime(expiration))
        w.WriteByte(0);                 // upgrade slots
        w.WriteByte(0);                 // level
        // 15 個 short: str,dex,int,luk,hp,mp,watk,matk,wdef,mdef,acc,avoid,hands,speed,jump
        for (var i = 0; i < 15; i++) w.WriteShort(0);
        w.WriteShort(0);                // owner = 空 MapleAsciiString (length 0)
        w.WriteShort(0);                // flag
        w.WriteByte(0);                 // incSkill (>0?1:0)
        w.WriteByte(0);                 // item level
        w.WriteInt(0);                  // item exp (int, 不是 short!)
        w.WriteLong(0);                 // tracking uniqueId (因 hasUniqueId=0 → Java 寫 item.getUniqueId())
        w.WriteLong(GetTime(-2));       // getTime(-2)
        w.WriteInt(-1);                 // -1
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
        // 對齊 Java addRingInfo: 4 個 short (前置 + cRing.size + fRing.size + marriage旗標)；先前只 3 個
        w.WriteShort(0);
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
