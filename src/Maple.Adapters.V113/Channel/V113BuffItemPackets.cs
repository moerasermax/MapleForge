using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113SolomonRequest(int Tick, short Slot, int ItemId);

internal readonly record struct V113GachExpRequest(int Tick);

internal readonly record struct V113TransformPlayerRequest(int Tick, short Slot, int ItemId, string TargetName);

internal readonly record struct V113XmasSurpriseRequest(long CashId);

/// <summary>v113 special buff/reward item packets. Mirrors Java PlayersHandler/CashShopOperation small handlers.</summary>
internal static class V113BuffItemPackets
{
    public const short RecvSolomon = unchecked((short)0x9B);
    public const short RecvGachExp = unchecked((short)0x9C);
    public const short RecvTransformPlayer = unchecked((short)0xA0);
    public const short RecvXmasSurprise = unchecked((short)0xA2);

    public const short SendModifyInventoryItem = 0x1B;
    public const short SendShowStatusInfo = 0x25;
    public const short SendGiveForeignBuff = unchecked((short)0xC0);
    public const short SendXmasSurprise = 0x161;

    public const int TransformPrankItemId = 2212000;
    public const int TransformPrankEffectItemId = 2210023;
    public const int XmasSurpriseBoxItemId = 5222000;
    public const int TransformPrankMorphId = 23;
    public const int TransformPrankDurationMilliseconds = 3_600_000;

    public static V113SolomonRequest ParseSolomon(PacketReader reader)
        => new(reader.ReadInt(), reader.ReadShort(), reader.ReadInt());

    public static V113GachExpRequest ParseGachExp(PacketReader reader)
        => new(reader.ReadInt());

    public static V113TransformPlayerRequest ParseTransformPlayer(PacketReader reader)
    {
        if (reader.Remaining <= 11)
        {
            throw new InvalidDataException("TRANSFORM_PLAYER body is shorter than the Java minimum.");
        }

        return new V113TransformPlayerRequest(
            reader.ReadInt(),
            reader.ReadShort(),
            reader.ReadInt(),
            reader.ReadMapleString());
    }

    public static V113XmasSurpriseRequest ParseXmasSurprise(PacketReader reader)
        => new(ReadLong(reader));

    public static int GetSolomonExperience(int itemId)
        => itemId switch
        {
            2370000 => 100_000,
            2370001 => 50_000,
            2370002 => 30_000,
            2370003 => 20_000,
            2370004 => 10_000,
            2370005 => 5_000,
            2370006 => 3_000,
            2370007 => 2_000,
            2370008 => 1_000,
            2370009 => 500,
            2370010 => 300,
            2370011 => 200,
            2370012 => 100,
            _ => 0,
        };

    public static MapleStatEffect? GetTransformEffect(int itemId)
        => itemId == TransformPrankItemId
            ? new MapleStatEffect
            {
                SourceId = TransformPrankEffectItemId,
                IsSkill = false,
                IsOverTime = true,
                DurationMilliseconds = TransformPrankDurationMilliseconds,
                Statups = new[] { new BuffStatValue(MapleBuffStat.MORPH, TransformPrankMorphId) },
            }
            : null;

    public static bool IsCashBlocked(int itemId)
        => itemId is 5222000 or 5500001 or 5500002 or 5600001 or 5252000
            or 5350003 or 5401000 or 5490000 or 5490001 or 5500000
            or 5252001 or 5252003 or 5220001 or 5220002 or 5200000
            or 5200001 or 5200002 or 5320000 or 5440000 or 5201001
            or 5201002;

    public static byte[] ModifyInventoryQuantity(InventoryType type, short slot, short newQuantity, bool removed)
    {
        var w = new PacketWriter(12);
        w.WriteShort(SendModifyInventoryItem);
        w.WriteByte(0);
        w.WriteByte(1);
        w.WriteByte(removed ? 3 : 1);
        w.WriteByte((byte)type);
        w.WriteShort(slot);
        if (!removed)
        {
            w.WriteShort(newQuantity);
        }

        return w.ToArray();
    }

    public static byte[] GainExpOthers(int gain, bool inChat = true, bool white = false)
    {
        var w = new PacketWriter(48);
        w.WriteShort(SendShowStatusInfo);
        w.WriteByte(3);
        w.WriteByte(white ? 1 : 0);
        w.WriteInt(gain);
        w.WriteByte(inChat ? 1 : 0);
        w.WriteInt(0);
        w.WriteByte(0);
        w.WriteShort(0);
        w.WriteZeroBytes(8);
        if (inChat)
        {
            w.WriteZeroBytes(4);
            w.WriteZeroBytes(10);
        }
        else
        {
            w.WriteInt(0);
            w.WriteZeroBytes(4);
        }

        w.WriteZeroBytes(4);
        w.WriteZeroBytes(4);
        return w.ToArray();
    }

    public static byte[] GiveForeignBuff(int characterId, IReadOnlyList<BuffStatValue> statups, bool isMorph)
    {
        var w = new PacketWriter(48);
        w.WriteShort(SendGiveForeignBuff);
        w.WriteInt(characterId);
        V113SkillPackets.WriteBuffMask(w, statups.Select(static s => s.Stat));
        foreach (var statup in statups)
        {
            w.WriteShort((short)statup.Value);
        }

        w.WriteShort(0);
        if (isMorph)
        {
            w.WriteByte(0);
        }

        w.WriteByte(0);
        w.WriteZeroBytes(20);
        return w.ToArray();
    }

    public static byte[] ShowXmasSurprise(bool full, long boxCashId, Item? item, int accountId)
    {
        var w = new PacketWriter(80);
        w.WriteShort(SendXmasSurprise);
        w.WriteByte(full ? 222 : 223);
        if (!full)
        {
            ArgumentNullException.ThrowIfNull(item);

            w.WriteLong(boxCashId);
            w.WriteInt(0);
            AddCashItemInfo(w, item, accountId, serialNumber: 0);
            w.WriteInt(item.ItemId);
            w.WriteByte(1);
            w.WriteByte(1);
        }

        return w.ToArray();
    }

    private static long ReadLong(PacketReader reader)
    {
        var low = (uint)reader.ReadInt();
        var high = reader.ReadInt();
        return ((long)high << 32) | low;
    }

    private static void AddCashItemInfo(PacketWriter w, Item item, int accountId, int serialNumber)
    {
        w.WriteLong(item.UniqueId > 0 ? item.UniqueId : 0);
        w.WriteLong(accountId);
        w.WriteInt(item.ItemId);
        w.WriteInt(serialNumber);
        w.WriteShort(item.Quantity);
        w.WriteFixedAsciiString(item.Owner, 15);
        w.WriteLong(GetExpirationTime(item.Expiration));
        w.WriteLong(0);
    }

    private static long GetExpirationTime(long realTimestamp)
    {
        const long fileTimeUnixOffset = 116444592000000000L;
        const long maxTime = 150842304000000000L;
        return realTimestamp == -1
            ? maxTime
            : (realTimestamp / 1000 * 10000000) + fileTimeUnixOffset;
    }
}
