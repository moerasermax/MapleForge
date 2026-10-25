using Maple.Core.Inventory;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P198：寵物道具的 itemInfo（Java <c>PacketHelper.addItemInfo</c> 的 pet 分支 → <c>addPetItemInfo</c>）：
/// type 3、itemId、hasUniqueId(+寵物 uniqueId)、expiration、名字（13 bytes 定長）、等級、親密度、飽食度、
/// 第二個 expiration（已過期寫 -1）、short 0、short flags、short 0、4×0。從 type byte 開始寫（位置由呼叫端）。
/// 時間照 Java <c>getTime</c>：-1 → MAX_TIME，否則 秒×10^7＋FT_UT_OFFSET。Java-source candidate／unverified。
/// </summary>
public static class V113PetItemCodec
{
    private const long FtUtOffset = 116444592000000000L;
    private const long MaxTime = 150842304000000000L;

    /// <summary>寵物道具（500xxxx）。</summary>
    public static bool IsPetItem(int itemId) => itemId / 10000 == 500;

    public static bool TryWrite(PacketWriter w, Item item)
    {
        if (!IsPetItem(item.ItemId))
        {
            return false;
        }

        Write(w, item.ItemId, item.UniqueId, item.Owner, item.HasPetStats ? item.PetLevel : (byte)1, item.HasPetStats ? item.PetCloseness : (short)0,
            item.HasPetStats ? item.PetFullness : (byte)100, item.Flag, item.Expiration);
        return true;
    }

    public static bool TryWrite(PacketWriter w, ItemRecord record)
    {
        if (!IsPetItem(record.ItemId))
        {
            return false;
        }

        var hasStats = record.PetLevel > 0;
        Write(w, record.ItemId, record.UniqueId, record.Owner, hasStats ? record.PetLevel : (byte)1, hasStats ? record.PetCloseness : (short)0,
            hasStats ? record.PetFullness : (byte)100, record.Flag, record.Expiration);
        return true;
    }

    public static long JavaTime(long realTimestamp) => realTimestamp == -1 ? MaxTime : (realTimestamp / 1000 * 10000000) + FtUtOffset;

    private static void Write(PacketWriter w, int itemId, long uniqueId, string? name, byte level, short closeness, byte fullness, short flags, long expiration)
    {
        w.WriteByte(3);
        w.WriteInt(itemId);
        w.WriteByte(uniqueId > 0 ? 1 : 0);
        if (uniqueId > 0)
        {
            w.WriteLong(uniqueId);
        }

        w.WriteLong(JavaTime(expiration));
        w.WriteFixedAsciiString(name ?? string.Empty, 13);
        w.WriteByte(level);
        w.WriteShort(closeness);
        w.WriteByte(fullness);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        w.WriteLong(JavaTime(expiration <= now ? -1 : expiration));
        w.WriteShort(0);
        w.WriteShort(flags);
        w.WriteShort(0);
        w.WriteInt(0);
    }
}
