using Maple.Application.Storage;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Storage;

namespace Maple.Adapters.V113.Channel;

internal enum StorageClientMode : byte
{
    TakeOut = 4,
    Store = 5,
    Arrange = 6,
    Meso = 7,
    Close = 8,
}

internal readonly record struct StorageRequest(
    StorageClientMode Mode,
    byte RawType = 0,
    byte StorageSlot = 0,
    short InventorySlot = 0,
    int ItemId = 0,
    short Quantity = 0,
    int Meso = 0)
{
    public InventoryType Type => RawType != 0 ? (InventoryType)RawType : InventoryTypeOf(ItemId);
    public bool HasValidType => RawType == 0 || InventoryTypes.IsValid(RawType);

    private static InventoryType InventoryTypeOf(int itemId)
    {
        var raw = itemId / 1_000_000;
        return raw is >= 1 and <= 5 ? (InventoryType)raw : InventoryType.Etc;
    }
}

/// <summary>
/// v113 倉庫封包。對照 Java NPCHandler.handleStorage / MaplePacketCreator.getStorage 系列。
/// </summary>
internal static class V113StoragePackets
{
    public const short RecvStorageOpcode = 0x37;
    public const short SendOpenStorageOpcode = 0x141;

    public static StorageRequest Parse(PacketReader reader)
    {
        var mode = (StorageClientMode)reader.ReadByte();
        return mode switch
        {
            StorageClientMode.TakeOut => new StorageRequest(
                mode,
                RawType: reader.ReadByte(),
                StorageSlot: reader.ReadByte()),

            StorageClientMode.Store => new StorageRequest(
                mode,
                InventorySlot: reader.ReadShort(),
                ItemId: reader.ReadInt(),
                Quantity: reader.ReadShort()),

            StorageClientMode.Meso => new StorageRequest(
                mode,
                Meso: reader.ReadInt()),

            _ => new StorageRequest(mode),
        };
    }

    public static byte[] Open(int npcId, StorageBox storage)
    {
        var w = new PacketWriter();
        w.WriteShort(SendOpenStorageOpcode);
        w.WriteByte(0x16);
        w.WriteInt(npcId);
        w.WriteByte(storage.Slots);
        w.WriteShort(0x7E);
        w.WriteShort(0);
        w.WriteInt(0);
        w.WriteInt(storage.Meso);
        w.WriteShort(0);
        WriteItems(w, storage.Items);
        w.WriteShort(0);
        w.WriteByte(0);
        return w.ToArray();
    }

    public static byte[] Full()
    {
        var w = new PacketWriter();
        w.WriteShort(SendOpenStorageOpcode);
        w.WriteByte(0x11);
        return w.ToArray();
    }

    public static byte[] Meso(StorageBox storage)
    {
        var w = new PacketWriter();
        w.WriteShort(SendOpenStorageOpcode);
        w.WriteByte(0x13);
        w.WriteByte(storage.Slots);
        w.WriteShort(2);
        w.WriteShort(0);
        w.WriteInt(0);
        w.WriteInt(storage.Meso);
        return w.ToArray();
    }

    public static byte[] Stored(StorageBox storage, InventoryType type) =>
        TypeList(0x0D, storage, type);

    public static byte[] TakenOut(StorageBox storage, InventoryType type) =>
        TypeList(0x09, storage, type);

    public static byte[] Arranged(StorageBox storage)
    {
        var w = new PacketWriter();
        w.WriteShort(SendOpenStorageOpcode);
        w.WriteByte(15);
        w.WriteByte(storage.Slots);
        w.WriteByte(124);
        w.WriteZeroBytes(10);
        WriteItems(w, storage.Items);
        w.WriteByte(0);
        return w.ToArray();
    }

    public static byte[]? EncodeResult(StorageResult result, int npcId, StorageBox storage) =>
        result.Kind switch
        {
            StorageResultKind.Opened => Open(npcId, storage),
            StorageResultKind.Full => Full(),
            StorageResultKind.Stored when result.ChangedType is { } type => Stored(storage, type),
            StorageResultKind.TakenOut when result.ChangedType is { } type => TakenOut(storage, type),
            StorageResultKind.MesoChanged => Meso(storage),
            StorageResultKind.Arranged => Arranged(storage),
            _ => null,
        };

    private static byte[] TypeList(byte responseMode, StorageBox storage, InventoryType type)
    {
        var w = new PacketWriter();
        w.WriteShort(SendOpenStorageOpcode);
        w.WriteByte(responseMode);
        w.WriteByte(storage.Slots);
        w.WriteShort(BitfieldEncoding(type));
        w.WriteShort(0);
        w.WriteInt(0);
        WriteItems(w, storage.ItemsByType(type));
        return w.ToArray();
    }

    private static void WriteItems(PacketWriter w, IReadOnlyCollection<Item> items)
    {
        w.WriteByte(items.Count);
        foreach (var item in items)
            WriteItemInfo(w, item);
    }

    private static void WriteItemInfo(PacketWriter w, Item item)
    {
        var record = ItemRecord.From(StorageBox.InventoryTypeOf(item), item);
        var hasUniqueId = record.UniqueId > 0;

        if (record.IsEquip)
        {
            w.WriteByte(1);
            w.WriteInt(record.ItemId);
            w.WriteByte(hasUniqueId ? 1 : 0);
            if (hasUniqueId) w.WriteLong(record.UniqueId);
            w.WriteLong(GetTime(record.Expiration));
            w.WriteByte(record.UpgradeSlots);
            w.WriteByte(record.Level);
            w.WriteShort(record.Str); w.WriteShort(record.Dex); w.WriteShort(record.Int); w.WriteShort(record.Luk);
            w.WriteShort(record.Hp); w.WriteShort(record.Mp); w.WriteShort(record.Watk); w.WriteShort(record.Matk);
            w.WriteShort(record.Wdef); w.WriteShort(record.Mdef); w.WriteShort(record.Acc); w.WriteShort(record.Avoid);
            w.WriteShort(record.Hands); w.WriteShort(record.Speed); w.WriteShort(record.Jump);
            w.WriteMapleString(record.Owner);
            w.WriteShort(record.Flag);
            w.WriteByte(0);
            w.WriteByte(record.ItemLevel);
            w.WriteInt(record.ItemExp);
            if (!hasUniqueId) w.WriteLong(record.UniqueId);
            w.WriteLong(GetTime(-2));
            w.WriteInt(-1);
            return;
        }

        w.WriteByte(2);
        w.WriteInt(record.ItemId);
        w.WriteByte(hasUniqueId ? 1 : 0);
        if (hasUniqueId) w.WriteLong(record.UniqueId);
        w.WriteLong(GetTime(record.Expiration));
        w.WriteShort(record.Quantity);
        w.WriteMapleString(record.Owner);
        w.WriteShort(record.Flag);
    }

    private static short BitfieldEncoding(InventoryType type) => (short)(2 << (byte)type);

    private static long GetTime(long realTimestamp)
    {
        const long ftUtOffset = 116444592000000000L;
        const long maxTime = 150842304000000000L;
        if (realTimestamp == -1) return maxTime;
        return (realTimestamp / 1000 * 10000000) + ftUtOffset;
    }
}
