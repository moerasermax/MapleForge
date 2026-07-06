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
    public void Opcodes_MatchJavaCashShopProperties()
    {
        Assert.Equal(0x20, V113ChannelRecvOp.EnterCashShop);
        Assert.Equal(0xE6, (ushort)V113ChannelRecvOp.CashShopOperation);
        Assert.Equal(0xE5, (ushort)V113ChannelRecvOp.CsUpdate);
        Assert.Equal(0x7D, V113ChannelSendOp.SetCashShop);
        Assert.Equal(0x0A, V113ChannelSendOp.CashShopUse);
        Assert.Equal(0x15F, V113ChannelSendOp.CashShopAccount);
        Assert.Equal(0x157, V113CashShopPackets.SendCashShopUpdate);
        Assert.Equal(0x158, V113CashShopPackets.SendCashShopOperation);
    }

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
    public void WarpCashShop_WritesSetCashShopCharacterAndEmptyCatalogSkeleton()
    {
        var character = NewCharacter(gender: 0);

        var packet = V113CashShopPackets.WarpCashShop(character, "cashacct");
        var reader = new PacketReader(packet);

        Assert.Equal(V113ChannelSendOp.SetCashShop, reader.ReadShort());
        Assert.Equal(-1L, BitConverter.ToInt64(packet, 2));
        Assert.Equal((byte)0, packet[10]);
        Assert.Equal(character.Id, BitConverter.ToInt32(packet, 11));

        var accountNameOffset = IndexOf(packet, "cashacct"u8.ToArray());
        Assert.True(accountNameOffset > 0);
        Assert.Equal((short)8, BitConverter.ToInt16(packet, accountNameOffset - 2));
        Assert.Equal(0, BitConverter.ToInt32(packet, accountNameOffset + 8));
        Assert.Equal(0, BitConverter.ToInt16(packet, accountNameOffset + 12));
    }

    [Fact]
    public void ShowCashShopAccount_WritesCsAcc()
    {
        byte[] expected =
        [
            0x5F, 0x01,
            0x01,
            0x04, 0x00,
            (byte)'t', (byte)'e', (byte)'s', (byte)'t',
        ];

        Assert.Equal(expected, V113CashShopPackets.ShowCashShopAccount("test"));
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
    public void ShowCouponRedeemedItem_WritesJavaMultiItemCouponLayout()
    {
        var item = new Item
        {
            ItemId = 2000000,
            Quantity = 3,
            UniqueId = 456,
            Expiration = -1,
        };

        var packet = V113CashShopPackets.ShowCouponRedeemedItem(
            item,
            accountId: 7,
            serialNumber: 10020030,
            maplePoints: 12,
            mesos: 34);

        // Java-source candidate/unverified: MTSCSPacket.showCouponRedeemedItem(Map, int, int, MapleClient);
        // no true v113 client capture has promoted this S2C fixture to golden truth.
        Assert.Equal(73, packet.Length);
        Assert.Equal(V113CashShopPackets.SendCashShopOperation, BitConverter.ToInt16(packet, 0));
        Assert.Equal(V113CashShopPackets.ServerCouponRedeemed, packet[2]);
        Assert.Equal(1, packet[3]);
        Assert.Equal(456, BitConverter.ToInt64(packet, 4));
        Assert.Equal(7, BitConverter.ToInt64(packet, 12));
        Assert.Equal(2000000, BitConverter.ToInt32(packet, 20));
        Assert.Equal(10020030, BitConverter.ToInt32(packet, 24));
        Assert.Equal(3, BitConverter.ToInt16(packet, 28));
        Assert.True(packet.Skip(30).Take(15).All(static b => b == 0));
        Assert.Equal(150842304000000000L, BitConverter.ToInt64(packet, 45));
        Assert.Equal(0, BitConverter.ToInt64(packet, 53));
        Assert.Equal(12, BitConverter.ToInt64(packet, 61));
        Assert.Equal(34, BitConverter.ToInt32(packet, 69));
    }

    [Fact]
    public void ShowGiftsEmpty_WritesJavaEmptyGiftList()
    {
        Assert.Equal(
            [0x58, 0x01, V113CashShopPackets.ServerShowGifts, 0x00, 0x00],
            V113CashShopPackets.ShowGiftsEmpty());
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
    public void EnableCashShopUse_WritesCsUse()
    {
        Assert.Equal(
            [0x0A, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00],
            V113CashShopPackets.EnableCashShopUse());
    }

    [Fact]
    public void ShowWishListEmpty_WritesTenZeroSerialNumbers()
    {
        var packet = V113CashShopPackets.ShowWishListEmpty();
        var reader = new PacketReader(packet);

        Assert.Equal(V113CashShopPackets.SendCashShopOperation, reader.ReadShort());
        Assert.Equal(V113CashShopPackets.ServerShowWishList, reader.ReadByte());
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(0, reader.ReadInt());
        }
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void InitialCashShopPackets_WritesJavaInitializationOrder()
    {
        var account = new Account { Id = 7, AccountName = "cashacct", CashPoints = 55, MaplePoints = 5 };
        var player = NewPlayer(gender: 0);

        var packets = V113CashShopPackets.InitialCashShopPackets(
            player.Character,
            account,
            player.Inventory.By(InventoryType.Cash).Items,
            storageSlots: 16,
            characterSlots: 3);

        Assert.Equal(7, packets.Count);
        Assert.Equal(V113ChannelSendOp.SetCashShop, BitConverter.ToInt16(packets[0], 0));
        Assert.Equal(V113ChannelSendOp.CashShopAccount, BitConverter.ToInt16(packets[1], 0));
        Assert.Equal(V113CashShopPackets.ServerShowGifts, packets[2][2]);
        Assert.Equal(V113CashShopPackets.ServerShowCashInventory, packets[3][2]);
        Assert.Equal(V113CashShopPackets.SendCashShopUpdate, BitConverter.ToInt16(packets[4], 0));
        Assert.Equal(V113ChannelSendOp.CashShopUse, BitConverter.ToInt16(packets[5], 0));
        Assert.Equal(V113CashShopPackets.ServerShowWishList, packets[6][2]);
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
            NewCharacter(gender),
            new Position(0, 0, 0, 0));

    private static Character NewCharacter(byte gender)
        => new()
        {
            Id = 1,
            AccountId = 7,
            Name = "CashShopAdapter",
            Gender = gender,
            MapId = 100000000,
            Level = 10,
        };

    private static int IndexOf(byte[] source, byte[] pattern)
    {
        for (var i = 0; i <= source.Length - pattern.Length; i++)
        {
            var found = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                return i;
            }
        }

        return -1;
    }

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
