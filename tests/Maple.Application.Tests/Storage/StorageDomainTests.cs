using Maple.Application.Storage;
using Maple.Core.Accounts;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Tests.Storage;

public sealed class StorageDomainTests
{
    [Fact]
    public void StoreAndTakeOutItem_RoundTripsBetweenInventoryAndAccountStorage()
    {
        var account = new Account { Id = 7 };
        var player = BuildPlayer(account, new ItemRecord
        {
            Type = (byte)InventoryType.Use,
            ItemId = 2000000,
            Slot = 1,
            Quantity = 30,
        });
        player.AttachStorage(account);

        Assert.True(player.TryStoreItemToStorage(InventoryType.Use, inventorySlot: 1, quantity: 20));
        Assert.Equal(10, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
        Assert.Single(player.Storage.Items);
        Assert.Equal(20, player.Storage.Items[0].Quantity);
        Assert.Single(account.Storage.Items);

        Assert.True(player.TryTakeItemFromStorage(InventoryType.Use, storageTypeSlot: 0));
        Assert.Empty(player.Storage.Items);
        Assert.Empty(account.Storage.Items);
        // P210：Java addFromDrop——疊回原本的堆疊。
        Assert.Equal(30, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(2));
    }

    [Fact]
    public void StorageMeso_DepositAndWithdraw_ClampInvalidOperations()
    {
        var account = new Account
        {
            Id = 7,
            Storage = new() { Meso = 200 },
        };
        var player = BuildPlayer(account);
        player.Character.Meso = 1500;
        player.AttachStorage(account);

        Assert.True(player.TryDepositStorageMeso(500));
        Assert.Equal(1000, player.Character.Meso);
        Assert.Equal(700, account.Storage.Meso);

        Assert.True(player.TryWithdrawStorageMeso(300));
        Assert.Equal(1300, player.Character.Meso);
        Assert.Equal(400, account.Storage.Meso);

        Assert.False(player.TryWithdrawStorageMeso(500));
        Assert.False(player.TryDepositStorageMeso(2000));
        Assert.Equal(1300, player.Character.Meso);
        Assert.Equal(400, account.Storage.Meso);
    }

    [Fact]
    public void AccountStorage_HydrateFlush_PreservesSlotsMesoAndOrderedItems()
    {
        var account = new Account
        {
            Id = 7,
            Storage = new()
            {
                Slots = 8,
                Meso = 1234,
                Items =
                {
                    new ItemRecord { Type = (byte)InventoryType.Etc, ItemId = 4000000, Slot = 1, Quantity = 3 },
                    new ItemRecord { Type = (byte)InventoryType.Use, ItemId = 2000000, Slot = 0, Quantity = 2 },
                },
            },
        };
        var player = BuildPlayer(account);
        player.AttachStorage(account);

        Assert.Equal(8, player.Storage.Slots);
        Assert.Equal(1234, player.Storage.Meso);
        Assert.Equal(2000000, player.Storage.Items[0].ItemId);
        Assert.Equal(4000000, player.Storage.Items[1].ItemId);

        player.FlushStorage(account);

        Assert.Equal(8, account.Storage.Slots);
        Assert.Equal(1234, account.Storage.Meso);
        Assert.Equal(new short[] { 0, 1 }, account.Storage.Items.Select(i => i.Slot).ToArray());
        Assert.Equal(new[] { 2000000, 4000000 }, account.Storage.Items.Select(i => i.ItemId).ToArray());
    }

    [Fact]
    public void StorageService_ReturnsFullWhenStorageHasNoFreeSlot()
    {
        var account = new Account
        {
            Id = 7,
            Storage = new()
            {
                Slots = 1,
                Items = { new ItemRecord { Type = (byte)InventoryType.Etc, ItemId = 4000000, Slot = 0, Quantity = 1 } },
            },
        };
        var player = BuildPlayer(account, new ItemRecord
        {
            Type = (byte)InventoryType.Etc,
            ItemId = 4000001,
            Slot = 1,
            Quantity = 1,
        });
        player.AttachStorage(account);

        var result = new StorageService().Store(player, InventoryType.Etc, 1, 1);

        Assert.Equal(StorageResultKind.Full, result.Kind);
    }

    [Fact]
    public void StorageService_RejectsStoreWhenExpectedItemIdDoesNotMatchSlot()
    {
        var account = new Account { Id = 7 };
        var player = BuildPlayer(account, new ItemRecord
        {
            Type = (byte)InventoryType.Use,
            ItemId = 2000000,
            Slot = 1,
            Quantity = 2,
        });
        player.AttachStorage(account);

        var result = new StorageService().Store(player, InventoryType.Use, 1, 1, expectedItemId: 2000001);

        Assert.Equal(StorageResultKind.None, result.Kind);
        Assert.Empty(player.Storage.Items);
        Assert.Equal(2, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
    }

    [Fact]
    public void TakeOut_FreeMarketFee_AndFailuresPutItemBack()
    {
        // P210：Java handleStorage mode 4——自由市場收 1000；楓幣不足／背包放不下時放回倉庫並 dropMessage(1)。
        var account = new Account { Id = 7 };
        var player = BuildPlayer(account, new ItemRecord { Type = (byte)InventoryType.Use, ItemId = 2000000, Slot = 1, Quantity = 30 });
        player.AttachStorage(account);
        Assert.True(player.TryStoreItemToStorage(InventoryType.Use, inventorySlot: 1, quantity: 30));
        player.Character.MapId = StorageService.FreeMarketMapId;
        player.Character.Meso = 999;
        var service = new StorageService();

        var poor = service.TakeOut(player, InventoryType.Use, 0);
        Assert.Equal(StorageService.NotEnoughMesoMessage, poor.PopupMessage);
        Assert.Single(player.Storage.Items);

        var bag = player.Inventory.By(InventoryType.Use);
        for (short slot = 1; slot <= bag.SlotLimit; slot++)
        {
            bag.Put(new Item { ItemId = 2010000 + slot, Quantity = 1, Slot = slot });
        }

        player.Character.Meso = 5000;
        var full = service.TakeOut(player, InventoryType.Use, 0);
        Assert.Equal(StorageService.NoSpaceMessage, full.PopupMessage);
        Assert.Single(player.Storage.Items);
        Assert.Equal(5000, player.Character.Meso);

        bag.TryTake(1, 1, out _);
        var ok = service.TakeOut(player, InventoryType.Use, 0);
        Assert.Equal(StorageResultKind.TakenOut, ok.Kind);
        Assert.True(ok.MesoChanged);
        Assert.Equal(4000, player.Character.Meso);
        Assert.Equal((short)30, Assert.Single(ok.Gained!.Added).Quantity);
        Assert.Empty(player.Storage.Items);
    }

    [Fact]
    public void Store_ChargesFee_ReportsRemoval_StarsStoreWholeStack()
    {
        // P211：Java handleStorage mode 5——手續費 100（自由市場 500）、removeFromSlot、飛鏢整組、寵物拒絕。
        var account = new Account { Id = 7 };
        var player = BuildPlayer(
            account,
            new ItemRecord { Type = (byte)InventoryType.Use, ItemId = 2000000, Slot = 1, Quantity = 30 },
            new ItemRecord { Type = (byte)InventoryType.Use, ItemId = 2070000, Slot = 2, Quantity = 500 },
            new ItemRecord { Type = (byte)InventoryType.Cash, ItemId = 5000000, Slot = 1, Quantity = 1 });
        player.AttachStorage(account);
        player.Character.Meso = 150;
        var service = new StorageService();

        var stored = service.Store(player, InventoryType.Use, 1, 20, 2000000);
        Assert.Equal(StorageResultKind.Stored, stored.Kind);
        Assert.True(stored.MesoChanged);
        Assert.Equal(50, player.Character.Meso);
        Assert.Equal((InventoryType.Use, (short)1, (short)30, (short)10), (stored.Removed!.Type, stored.Removed.Slot, stored.Removed.OldQuantity, stored.Removed.NewQuantity));

        Assert.Equal(StorageService.NotEnoughMesoMessage, service.Store(player, InventoryType.Use, 1, 5, 2000000).PopupMessage);

        player.Character.Meso = 1000;
        var star = service.Store(player, InventoryType.Use, 2, 1, 2070000);
        Assert.Equal((short)0, star.Removed!.NewQuantity);
        Assert.Equal(500, player.Storage.Items.Single(i => i.ItemId == 2070000).Quantity);

        Assert.True(service.Store(player, InventoryType.Cash, 1, 1, 5000000).EnableActions); // 寵物不可存
        player.Character.MapId = StorageService.FreeMarketMapId;
        service.Store(player, InventoryType.Use, 1, 1, 2000000);
        Assert.Equal(400, player.Character.Meso);
    }

    private static Player BuildPlayer(Account account, params ItemRecord[] inventoryItems)
    {
        var character = new Character
        {
            Id = 100,
            AccountId = account.Id,
            Name = "StorageTest",
            Items = inventoryItems.ToList(),
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }
}
