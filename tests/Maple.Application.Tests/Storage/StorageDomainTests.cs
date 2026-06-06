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
        Assert.Equal(10, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
        Assert.Equal(20, player.Inventory.By(InventoryType.Use).Get(2)!.Quantity);
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
