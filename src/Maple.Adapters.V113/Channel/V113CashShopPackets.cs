using Maple.Application.CashShop;
using Maple.Core.Accounts;
using Maple.Core.CashShop;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113CashShopPurchaseRequest(
    byte Action,
    CashCurrencyType Currency,
    int SerialNumber);

public sealed record V113CashShopOperationResult(
    bool Handled,
    bool AccountMutated,
    bool CharacterMutated,
    IReadOnlyList<byte[]> Packets);

/// <summary>v113 Cash Shop 封包。購買欄位對照 Java CashShopOperation.BuyCashItem / MTSCSPacket。</summary>
internal static class V113CashShopPackets
{
    public const short RecvCashShopOperation = unchecked((short)0xE6);
    public const short SendCashShopUpdate = 0x157;
    public const short SendCashShopOperation = 0x158;

    public const byte ClientBuyItem = 0x03;
    public const byte ServerShowCashInventory = 0x46;
    public const byte ServerBoughtCashItem = 0x4E;
    public const byte ServerBoughtCashItemFailed = 0x4F;

    public static V113CashShopPurchaseRequest? ParsePurchase(PacketReader reader)
    {
        var action = reader.ReadByte();
        if (action != ClientBuyItem)
        {
            return null;
        }

        var currency = (CashCurrencyType)(reader.ReadByte() + 1);
        var serialNumber = reader.ReadInt();
        return new V113CashShopPurchaseRequest(action, currency, serialNumber);
    }

    public static byte[] ShowBoughtCashItem(Item item, int serialNumber, int accountId)
    {
        var w = new PacketWriter();
        w.WriteShort(SendCashShopOperation);
        w.WriteByte(ServerBoughtCashItem);
        AddCashItemInfo(w, item, accountId, serialNumber);
        return w.ToArray();
    }

    public static byte[] SendCashShopFail(int errorCode)
    {
        var w = new PacketWriter();
        w.WriteShort(SendCashShopOperation);
        w.WriteByte(ServerBoughtCashItemFailed);
        w.WriteShort(errorCode);
        if (errorCode is 193 or 194)
        {
            w.WriteInt(errorCode);
        }

        return w.ToArray();
    }

    public static byte[] ShowCashBalances(Account account)
    {
        var w = new PacketWriter();
        w.WriteShort(SendCashShopUpdate);
        w.WriteInt(account.CashPoints);
        w.WriteInt(account.MaplePoints);
        return w.ToArray();
    }

    public static byte[] ShowCashInventory(IEnumerable<Item> items, int accountId, short storageSlots, short characterSlots)
    {
        var materialized = items.ToArray();
        var w = new PacketWriter();
        w.WriteShort(SendCashShopOperation);
        w.WriteByte(ServerShowCashInventory);
        w.WriteShort(materialized.Length);
        foreach (var item in materialized)
        {
            AddCashItemInfo(w, item, accountId, serialNumber: 0);
        }

        w.WriteShort(storageSlots);
        w.WriteShort(characterSlots);
        return w.ToArray();
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
        if (realTimestamp == -1)
        {
            return maxTime;
        }

        return (realTimestamp / 1000 * 10000000) + fileTimeUnixOffset;
    }
}

public sealed class V113CashShopOperationHandler
{
    private readonly CashShopService _cashShop;

    public V113CashShopOperationHandler(CashShopService cashShop)
    {
        _cashShop = cashShop;
    }

    public V113CashShopOperationResult Handle(PacketReader reader, Account account, Player player)
    {
        V113CashShopPurchaseRequest? request;
        try
        {
            request = V113CashShopPackets.ParsePurchase(reader);
        }
        catch (InvalidDataException)
        {
            return new V113CashShopOperationResult(false, false, false, Array.Empty<byte[]>());
        }

        if (request is null)
        {
            return new V113CashShopOperationResult(false, false, false, Array.Empty<byte[]>());
        }

        var result = _cashShop.Buy(account, player, request.Value.Currency, request.Value.SerialNumber);
        if (result.Status != CashShopTransactionStatus.Success || result.GainedItem is null)
        {
            return new V113CashShopOperationResult(
                true,
                false,
                false,
                new[] { V113CashShopPackets.SendCashShopFail(result.JavaErrorCode) });
        }

        return new V113CashShopOperationResult(
            true,
            true,
            true,
            new[]
            {
                V113CashShopPackets.ShowBoughtCashItem(
                    result.GainedItem,
                    result.SerialNumber,
                    account.Id),
                V113CashShopPackets.ShowCashBalances(account),
            });
    }
}
