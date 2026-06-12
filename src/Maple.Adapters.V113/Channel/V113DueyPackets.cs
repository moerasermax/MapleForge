using Maple.Application.Duey;
using Maple.Core.Duey;
using Maple.Core.Inventory;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal enum V113DueyClientOperation : byte
{
    SecondPassword = 1,
    SendPackage = 3,
    ReceivePackage = 5,
    ReturnPackage = 6,
    Close = 8,
}

internal readonly record struct V113DueyAction(
    V113DueyClientOperation Operation,
    DueySendRequest? SendRequest = null,
    int PackageId = 0,
    bool InvalidInventoryType = false,
    string SecondPassword = "");

/// <summary>v113 Duey 宅配封包。對照 Java DueyHandler / MaplePacketCreator.sendDuey。</summary>
internal static class V113DueyPackets
{
    public const short RecvDueyAction = 0x3B;
    public const short SendDuey = 0x155;

    public const byte OperationOpenSecondPassword = 9;
    public const byte OperationInbox = 10;
    public const byte OperationRemovePackage = 0x18;

    public const byte StatusNotEnoughMeso = 12;
    public const byte StatusNameDoesNotExist = 14;
    public const byte StatusSameAccount = 15;
    public const byte StatusNotEnoughSpace = 16;
    public const byte StatusUnsuccessful = 17;
    public const byte StatusSuccessful = 19;

    private const long FileTimeUnixOffset = 116444736000000000L;
    private const long ItemFileTimeUnixOffset = 116444592000000000L;
    private const long MaxTime = 150842304000000000L;

    public static V113DueyAction ParseAction(PacketReader reader)
    {
        var operation = (V113DueyClientOperation)reader.ReadByte();
        return operation switch
        {
            V113DueyClientOperation.SecondPassword => new V113DueyAction(
                operation,
                SecondPassword: reader.Remaining > 0 ? reader.ReadMapleString() : string.Empty),

            V113DueyClientOperation.SendPackage => ParseSendPackage(reader, operation),

            V113DueyClientOperation.ReceivePackage or V113DueyClientOperation.ReturnPackage =>
                new V113DueyAction(operation, PackageId: reader.ReadInt()),

            V113DueyClientOperation.Close => new V113DueyAction(operation),

            _ => new V113DueyAction(operation),
        };
    }

    public static byte[] OpenSecondPassword()
    {
        var w = new PacketWriter(4);
        w.WriteShort(SendDuey);
        w.WriteByte(OperationOpenSecondPassword);
        w.WriteByte(1);
        return w.ToArray();
    }

    public static byte[] Status(byte status)
    {
        var w = new PacketWriter(3);
        w.WriteShort(SendDuey);
        w.WriteByte(status);
        return w.ToArray();
    }

    public static byte[] Inbox(IReadOnlyList<DueyPackage> packages)
    {
        var count = Math.Min(packages.Count, byte.MaxValue);
        var w = new PacketWriter(64 + count * 256);
        w.WriteShort(SendDuey);
        w.WriteByte(OperationInbox);
        w.WriteByte(0);
        w.WriteByte(count);

        for (var i = 0; i < count; i++)
        {
            var package = packages[i];
            w.WriteInt(package.Id);
            w.WriteFixedAsciiString(package.SenderName, 15);
            w.WriteInt(package.Meso);
            w.WriteLong(ToFileTimestamp(package.ExpiresAtUnixMillis));
            w.WriteShort(0);
            w.WriteFixedAsciiString("13" + package.Message, 193);
            w.WriteZeroBytes(10);

            if (package.Item is { } item)
            {
                w.WriteByte(1);
                WriteItemInfo(w, item);
            }
            else
            {
                w.WriteByte(0);
            }
        }

        w.WriteByte(0);
        return w.ToArray();
    }

    public static byte[] RemovePackage(bool returnedOrDeleted, int packageId)
    {
        var w = new PacketWriter(8);
        w.WriteShort(SendDuey);
        w.WriteByte(OperationRemovePackage);
        w.WriteInt(packageId);
        w.WriteByte(returnedOrDeleted ? 3 : 4);
        return w.ToArray();
    }

    public static byte[] ModifyInventoryQuantity(DueyInventoryMutation mutation)
    {
        var mode = mutation.Removed ? 3 : 1;
        var w = BeginSingleInventoryModify(mode, mutation.Type, mutation.Slot);
        if (!mutation.Removed)
        {
            w.WriteShort(mutation.NewQuantity);
        }

        return w.ToArray();
    }

    public static byte[] ModifyInventoryAdd(InventoryType type, Item item)
    {
        var w = BeginSingleInventoryModify(mode: 0, type, item.Slot);
        WriteItemInfo(w, ItemRecord.From(type, item));
        return w.ToArray();
    }

    public static byte[] UpdateMeso(int meso) => V113ShopPackets.UpdateMeso(meso);

    public static byte StatusFor(DueyResultStatus status) => status switch
    {
        DueyResultStatus.Success => StatusSuccessful,
        DueyResultStatus.NotEnoughMeso => StatusNotEnoughMeso,
        DueyResultStatus.RecipientNotFound => StatusNameDoesNotExist,
        DueyResultStatus.SameAccount => StatusSameAccount,
        DueyResultStatus.InventoryFull => StatusNotEnoughSpace,
        _ => StatusUnsuccessful,
    };

    private static V113DueyAction ParseSendPackage(PacketReader reader, V113DueyClientOperation operation)
    {
        var inventoryId = reader.ReadByte();
        var itemSlot = reader.ReadShort();
        var amount = reader.ReadShort();
        var meso = reader.ReadInt();
        var recipient = reader.ReadMapleString();
        var quickDelivery = reader.ReadByte() > 0;
        var message = quickDelivery && reader.Remaining > 0 ? reader.ReadMapleString() : string.Empty;

        InventoryType? itemType = null;
        if (inventoryId > 0)
        {
            if (!InventoryTypes.IsValid(inventoryId))
            {
                return new V113DueyAction(operation, InvalidInventoryType: true);
            }

            itemType = (InventoryType)inventoryId;
        }

        return new V113DueyAction(
            operation,
            SendRequest: new DueySendRequest(itemType, itemSlot, amount, meso, recipient, quickDelivery, message));
    }

    private static PacketWriter BeginSingleInventoryModify(int mode, InventoryType type, short slot)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.ModifyInventoryItem);
        w.WriteByte(0);
        w.WriteByte(1);
        w.WriteByte(mode);
        w.WriteByte((byte)type);
        w.WriteShort(slot);
        return w;
    }

    private static void WriteItemInfo(PacketWriter w, ItemRecord record)
    {
        var hasUniqueId = record.UniqueId > 0;
        w.WriteByte(record.IsEquip ? 1 : 2);
        w.WriteInt(record.ItemId);
        w.WriteByte(hasUniqueId ? 1 : 0);
        if (hasUniqueId)
        {
            w.WriteLong(record.UniqueId);
        }

        w.WriteLong(ToItemTime(record.Expiration));
        if (record.IsEquip)
        {
            w.WriteByte(record.UpgradeSlots);
            w.WriteByte(record.Level);
            w.WriteShort(record.Str);
            w.WriteShort(record.Dex);
            w.WriteShort(record.Int);
            w.WriteShort(record.Luk);
            w.WriteShort(record.Hp);
            w.WriteShort(record.Mp);
            w.WriteShort(record.Watk);
            w.WriteShort(record.Matk);
            w.WriteShort(record.Wdef);
            w.WriteShort(record.Mdef);
            w.WriteShort(record.Acc);
            w.WriteShort(record.Avoid);
            w.WriteShort(record.Hands);
            w.WriteShort(record.Speed);
            w.WriteShort(record.Jump);
            w.WriteMapleString(record.Owner);
            w.WriteShort(record.Flag);
            w.WriteByte(0);
            w.WriteByte(record.ItemLevel);
            w.WriteInt(record.ItemExp);
            if (!hasUniqueId)
            {
                w.WriteLong(record.UniqueId);
            }

            w.WriteLong(ToItemTime(-2));
            w.WriteInt(-1);
            return;
        }

        w.WriteShort(record.Quantity);
        w.WriteMapleString(record.Owner);
        w.WriteShort(record.Flag);
    }

    private static long ToFileTimestamp(long unixMillis) => unixMillis * 10000 + FileTimeUnixOffset;

    private static long ToItemTime(long unixMillis)
    {
        if (unixMillis == -1)
        {
            return MaxTime;
        }

        return unixMillis / 1000 * 10000000 + ItemFileTimeUnixOffset;
    }
}
