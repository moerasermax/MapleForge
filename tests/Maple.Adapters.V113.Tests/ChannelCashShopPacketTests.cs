using Maple.Adapters.V113.Channel;
using Maple.Application.CashShop;
using Maple.Core.Accounts;
using Maple.Core.CashShop;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelCashShopPacketTests
{
    [Fact]
    public void ParsePurchase_ReadsJavaBuyItemLayout()
    {
        var w = new PacketWriter();
        w.WriteByte(V113CashShopPackets.ClientBuyItem);
        w.WriteByte(0);
        w.WriteInt(10000001);

        var request = V113CashShopPackets.ParsePurchase(new PacketReader(w.ToArray()));

        Assert.NotNull(request);
        Assert.Equal(V113CashShopPackets.ClientBuyItem, request!.Value.Action);
        Assert.Equal(CashCurrencyType.Cash, request.Value.Currency);
        Assert.Equal(10000001, request.Value.SerialNumber);
    }

    [Fact]
    public void ShowBoughtCashItem_WritesJavaCashItemInfoLayout()
    {
        var item = new Item
        {
            ItemId = 5350000,
            Quantity = 10,
            UniqueId = 123,
            Expiration = -1,
        };

        var packet = V113CashShopPackets.ShowBoughtCashItem(item, 10000001, accountId: 7);

        Assert.Equal(60, packet.Length);
        Assert.Equal(V113CashShopPackets.SendCashShopOperation, BitConverter.ToInt16(packet, 0));
        Assert.Equal(V113CashShopPackets.ServerBoughtCashItem, packet[2]);
        Assert.Equal(123, BitConverter.ToInt64(packet, 3));
        Assert.Equal(7, BitConverter.ToInt64(packet, 11));
        Assert.Equal(5350000, BitConverter.ToInt32(packet, 19));
        Assert.Equal(10000001, BitConverter.ToInt32(packet, 23));
        Assert.Equal(10, BitConverter.ToInt16(packet, 27));
        Assert.True(packet.Skip(29).Take(15).All(static b => b == 0));
        Assert.Equal(150842304000000000L, BitConverter.ToInt64(packet, 44));
        Assert.Equal(0, BitConverter.ToInt64(packet, 52));
    }

    [Fact]
    public void ShowCashBalances_WritesCsUpdate()
    {
        byte[] expected =
        {
            0x57, 0x01,
            0x37, 0x00, 0x00, 0x00,
            0x05, 0x00, 0x00, 0x00,
        };

        var packet = V113CashShopPackets.ShowCashBalances(new Account { CashPoints = 55, MaplePoints = 5 });

        Assert.Equal(expected, packet);
    }

    [Fact]
    public void SendCashShopFail_WritesBoughtCashItemFail()
    {
        byte[] expected =
        {
            0x58, 0x01,
            0x4F,
            0xA8, 0x00,
        };

        Assert.Equal(expected, V113CashShopPackets.SendCashShopFail(168));
    }

    [Fact]
    public void Handler_PurchaseSuccess_MutatesAccountPlayerAndReturnsConfirmPackets()
    {
        var service = new CashShopService(new FakeCashItemCatalog(DefaultItem));
        var handler = new V113CashShopOperationHandler(service);
        var account = new Account { Id = 7, CashPoints = 100, MaplePoints = 30 };
        var player = NewPlayer(gender: 0);
        var body = PurchaseBody(useNxMinusOne: 0, DefaultItem.SerialNumber);

        var result = handler.Handle(new PacketReader(body), account, player);

        Assert.True(result.Handled);
        Assert.True(result.AccountMutated);
        Assert.True(result.CharacterMutated);
        Assert.Equal(2, result.Packets.Count);
        Assert.Equal(55, account.CashPoints);
        Assert.Equal(10, player.Inventory.By(InventoryType.Cash).CountById(DefaultItem.ItemId));
        Assert.Equal(V113CashShopPackets.ServerBoughtCashItem, result.Packets[0][2]);
        Assert.Equal(V113CashShopPackets.SendCashShopUpdate, BitConverter.ToInt16(result.Packets[1], 0));
    }

    [Fact]
    public void Handler_NotEnoughCash_ReturnsJavaFailPacketWithoutMutation()
    {
        var service = new CashShopService(new FakeCashItemCatalog(DefaultItem));
        var handler = new V113CashShopOperationHandler(service);
        var account = new Account { Id = 7, CashPoints = 44 };
        var player = NewPlayer(gender: 0);

        var result = handler.Handle(new PacketReader(PurchaseBody(0, DefaultItem.SerialNumber)), account, player);

        Assert.True(result.Handled);
        Assert.False(result.AccountMutated);
        Assert.False(result.CharacterMutated);
        var packet = Assert.Single(result.Packets);
        Assert.Equal(V113CashShopPackets.ServerBoughtCashItemFailed, packet[2]);
        Assert.Equal(168, BitConverter.ToInt16(packet, 3));
        Assert.Equal(44, account.CashPoints);
        Assert.Empty(player.Character.Items);
    }

    private static byte[] PurchaseBody(byte useNxMinusOne, int serialNumber)
    {
        var w = new PacketWriter();
        w.WriteByte(V113CashShopPackets.ClientBuyItem);
        w.WriteByte(useNxMinusOne);
        w.WriteInt(serialNumber);
        return w.ToArray();
    }

    private static readonly CashItemDefinition DefaultItem = new(
        10000001,
        5350000,
        10,
        45,
        0,
        2,
        -1,
        true);

    private static Player NewPlayer(byte gender)
        => new(
            new Character
            {
                Id = 1,
                Name = "CashShopAdapter",
                Gender = gender,
            },
            new Position(0, 0, 0, 0));

    private sealed class FakeCashItemCatalog : ICashItemCatalog
    {
        private readonly Dictionary<int, CashItemDefinition> _items;

        public FakeCashItemCatalog(params CashItemDefinition[] items)
        {
            _items = items.ToDictionary(static i => i.SerialNumber);
        }

        public CashItemDefinition? GetBySerialNumber(int serialNumber)
            => _items.GetValueOrDefault(serialNumber);
    }
}
