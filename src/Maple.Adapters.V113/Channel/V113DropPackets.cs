using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113ItemPickupRequest(int Tick, Position ClientPosition, int ObjectId);

internal readonly record struct V113MesoDropRequest(int Tick, int Meso);

/// <summary>v113 掉落/拾取封包。對照 Java MaplePacketCreator drop/remove/show exp gain。</summary>
internal static class V113DropPackets
{
    public const short RecvItemPickup = unchecked((short)0xC6);
    public const short SendShowStatusInfo = 0x25;
    public const short SendDropItemFromMapObject = 0x107;
    public const short SendRemoveItemFromMap = 0x108;
    public const short SendUpdateStats = 0x1D;
    public const short SendModifyInventoryItem = 0x1B;

    private const int ExpStat = 0x10000;
    private const int MesoStat = 0x40000;

    public static V113ItemPickupRequest ParseItemPickup(PacketReader reader)
    {
        var tick = reader.ReadInt();
        reader.Skip(1);
        var x = reader.ReadShort();
        var y = reader.ReadShort();
        var objectId = reader.ReadInt();
        return new V113ItemPickupRequest(tick, new Position(x, y, 0, 0), objectId);
    }

    public static V113MesoDropRequest ParseMesoDrop(PacketReader reader)
    {
        var tick = reader.ReadInt();
        var meso = reader.ReadInt();
        return new V113MesoDropRequest(tick, meso);
    }

    public static byte[] DropItemFromMapObject(MapDrop drop, byte mode = 1)
    {
        var w = new PacketWriter(48);
        w.WriteShort(SendDropItemFromMapObject);
        w.WriteByte(mode);
        w.WriteInt(drop.ObjectId);
        w.WriteByte(drop.IsMeso ? 1 : 0);
        w.WriteInt(drop.ItemId);
        w.WriteInt(drop.OwnerId);
        w.WriteByte(drop.DropType);
        WritePos(w, drop.Position);
        w.WriteInt(drop.DropType == 0 ? drop.OwnerId : 0);

        if (mode != 2)
        {
            WritePos(w, drop.SourcePosition);
            w.WriteShort(0);
        }

        if (!drop.IsMeso)
        {
            w.WriteLong(GetTime(drop.Item?.Expiration ?? -1));
        }

        w.WriteShort(drop.PlayerDrop ? 0 : 1);
        return w.ToArray();
    }

    public static byte[] RemoveItemFromMap(int objectId, byte animation = 2, int characterId = 0, byte petSlot = 0)
    {
        var w = new PacketWriter(12);
        w.WriteShort(SendRemoveItemFromMap);
        w.WriteByte(animation);
        w.WriteInt(objectId);
        if (animation >= 2)
        {
            w.WriteInt(characterId);
            if (animation == 5)
            {
                w.WriteByte(petSlot);
            }
        }

        return w.ToArray();
    }

    public static byte[] ShowExpGainMonster(int gain, bool white = true)
    {
        var w = new PacketWriter(36);
        w.WriteShort(SendShowStatusInfo);
        w.WriteByte(3);
        w.WriteByte(white ? 1 : 0);
        w.WriteInt(gain);
        w.WriteByte(0);
        w.WriteInt(0);
        w.WriteByte(0);
        w.WriteByte(0);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteByte(0);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteInt(0);
        return w.ToArray();
    }

    public static byte[] ShowMesoGain(int gain, bool inChat = false)
    {
        var w = new PacketWriter(12);
        w.WriteShort(SendShowStatusInfo);
        if (inChat)
        {
            w.WriteByte(5);
            w.WriteInt(gain);
        }
        else
        {
            w.WriteByte(0);
            w.WriteByte(1);
            w.WriteByte(0);
            w.WriteInt(gain);
            w.WriteShort(0);
        }

        return w.ToArray();
    }

    public static byte[] ShowItemGain(int itemId, short quantity, bool inChat = false)
    {
        var w = new PacketWriter(16);
        if (inChat)
        {
            w.WriteShort(0xC7);
            w.WriteByte(3);
            w.WriteByte(1);
            w.WriteInt(itemId);
            w.WriteInt(quantity);
        }
        else
        {
            w.WriteShort(SendShowStatusInfo);
            w.WriteShort(0);
            w.WriteInt(itemId);
            w.WriteInt(quantity);
        }

        return w.ToArray();
    }

    public static byte[] UpdateExp(int exp, bool itemReaction = false)
        => UpdateStat(ExpStat, exp, itemReaction);

    public static byte[] UpdateMeso(int meso, bool itemReaction = true)
        => UpdateStat(MesoStat, meso, itemReaction);

    public static byte[] ModifyInventoryAdd(InventoryType type, Item item)
    {
        var w = BeginSingleInventoryModify(mode: 0, type, item.Slot);
        AddItemInfo(w, item);
        return w.ToArray();
    }

    private static byte[] UpdateStat(int stat, int value, bool itemReaction)
    {
        var w = new PacketWriter(11);
        w.WriteShort(SendUpdateStats);
        w.WriteByte(itemReaction ? 1 : 0);
        w.WriteInt(stat);
        w.WriteInt(value);
        return w.ToArray();
    }

    private static PacketWriter BeginSingleInventoryModify(int mode, InventoryType type, short slot)
    {
        var w = new PacketWriter();
        w.WriteShort(SendModifyInventoryItem);
        w.WriteByte(0);
        w.WriteByte(1);
        w.WriteByte(mode);
        w.WriteByte((byte)type);
        w.WriteShort(slot);
        return w;
    }

    private static void AddItemInfo(PacketWriter w, Item item)
    {
        w.WriteByte(item.IsEquip ? 1 : 2);
        w.WriteInt(item.ItemId);
        w.WriteByte(0);
        w.WriteLong(GetTime(item.Expiration));

        if (item is Equip equip)
        {
            w.WriteByte(equip.UpgradeSlots);
            w.WriteByte(equip.Level);
            w.WriteShort(equip.Str);
            w.WriteShort(equip.Dex);
            w.WriteShort(equip.Int);
            w.WriteShort(equip.Luk);
            w.WriteShort(equip.Hp);
            w.WriteShort(equip.Mp);
            w.WriteShort(equip.Watk);
            w.WriteShort(equip.Matk);
            w.WriteShort(equip.Wdef);
            w.WriteShort(equip.Mdef);
            w.WriteShort(equip.Acc);
            w.WriteShort(equip.Avoid);
            w.WriteShort(equip.Hands);
            w.WriteShort(equip.Speed);
            w.WriteShort(equip.Jump);
            w.WriteMapleString(equip.Owner);
            w.WriteShort(equip.Flag);
            w.WriteByte(0);
            w.WriteByte(equip.ItemLevel);
            w.WriteInt(equip.ItemExp);
            w.WriteLong(equip.UniqueId);
            w.WriteLong(GetTime(-2));
            w.WriteInt(-1);
        }
        else
        {
            w.WriteShort(item.Quantity);
            w.WriteMapleString(item.Owner);
            w.WriteShort(item.Flag);
        }
    }

    private static void WritePos(PacketWriter w, Position pos)
    {
        w.WriteShort(pos.X);
        w.WriteShort(pos.Y);
    }

    private static long GetTime(long offset)
    {
        const long KoreanEpochOffset = 116444736000000000L;
        if (offset < 0)
        {
            return KoreanEpochOffset + offset;
        }

        return KoreanEpochOffset + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 10000);
    }
}
