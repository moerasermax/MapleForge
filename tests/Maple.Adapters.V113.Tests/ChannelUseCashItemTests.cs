using Maple.Adapters.V113.Channel;
using Maple.Application.NpcItemServices;
using Maple.Application.Pets;
using Maple.Application.Social;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.NpcItemServices;
using Maple.Core.Pets;
using Maple.Core.Social;
using Maple.Core.World;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelUseCashItemTests
{
    [Fact]
    public void ParseUseCashItem_ReadsSlotAndItemId()
    {
        var body = new PacketWriter()
            .WriteShort(3)          // slot
            .WriteInt(5230000)      // itemId
            .WriteInt(2000000)      // searchItemId (for Owl)
            .ToArray();

        var reader = new PacketReader(body);
        var slot = reader.ReadShort();
        var itemId = reader.ReadInt();

        Assert.Equal(3, slot);
        Assert.Equal(5230000, itemId);
    }

    [Fact]
    public void OwlRouting_WithCashOwlItem_ProducesOwlSearchedPacket()
    {
        var player = CreateCashOwlPlayer(910000000);
        var handler = CreateHandlerWithResults();
        var body = new PacketWriter()
            .WriteShort(1)          // slot
            .WriteInt(OwlService.CashOwlItemId)  // 5230000
            .WriteInt(2000000)      // searchItemId
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        // Java order: OwlSearched first, then ModifyInventory (consume), then EnableActions
        Assert.Equal(3, result.Packets.Count);
        Assert.Equal(V113OwlPackets.SendShopScannerResult, BitConverter.ToInt16(result.Packets[0], 0));
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.Packets[1], 0));
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[2], 0));
    }

    [Fact]
    public void OwlRouting_EmptyResults_ReturnsEnableActions_NoConsumption()
    {
        var player = CreateCashOwlPlayer(910000000);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(OwlService.CashOwlItemId)
            .WriteInt(2000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        // Item should NOT be consumed when search returns empty
        var item = player.Inventory.By(InventoryType.Cash).Get(1);
        Assert.NotNull(item);
        Assert.Equal(1, item.Quantity);
    }

    [Fact]
    public void UnknownItemId_ReturnsEnableActions()
    {
        var player = CreatePlayerWithCashItem(910000000, 5999999, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5999999)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[0], 0));
    }

    [Fact]
    public void MissingItem_ReturnsEnableActions()
    {
        // Player has no cash items at all
        var character = new Character
        {
            Id = 1,
            Name = "NoCashItems",
            MapId = 910000000,
            Items = [],
        };
        var player = new Player(character, new Position(0, 0, 0, 0));
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(OwlService.CashOwlItemId)
            .WriteInt(2000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[0], 0));
    }

    [Fact]
    public void MismatchedItemId_ReturnsEnableActions()
    {
        // Player has a different item at the specified slot
        var player = CreatePlayerWithCashItem(910000000, 5100000, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(OwlService.CashOwlItemId)  // claims 5230000 but slot has 5100000
            .WriteInt(2000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[0], 0));
    }

    [Fact]
    public void OwlRouting_ConsumesOneCashItem()
    {
        var player = CreateCashOwlPlayer(910000000);
        var handler = CreateHandlerWithResults();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(OwlService.CashOwlItemId)
            .WriteInt(2000000)
            .ToArray();

        handler.Handle(new PacketReader(body), player);

        // Item quantity should be 0 after consumption
        var item = player.Inventory.By(InventoryType.Cash).Get(1);
        Assert.NotNull(item);
        Assert.Equal(0, item.Quantity);
    }

    [Fact]
    public void OwlRouting_NotInFreeMarket_ReturnsEnableActions()
    {
        // Player is in a normal map, not Free Market
        var player = CreateCashOwlPlayer(100000000);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(OwlService.CashOwlItemId)
            .WriteInt(2000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        // Item should NOT be consumed
        var item = player.Inventory.By(InventoryType.Cash).Get(1);
        Assert.NotNull(item);
    }

    [Fact]
    public void InvalidSlot_ReturnsEnableActions()
    {
        var player = CreatePlayerWithCashItem(910000000, 5100000, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(0)
            .WriteInt(5100000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void Chalkboard_SetsMessageAndDoesNotConsumeItem()
    {
        var player = CreatePlayerWithCashItem(910000000, 5370000, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5370000)
            .WriteMapleString("shop")
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Equal("shop", player.ChalkboardMessage);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
        Assert.Single(result.MapPackets);
        Assert.Equal(V113ChannelSendOp.Chalkboard, BitConverter.ToInt16(result.MapPackets[0], 0));
    }

    [Fact]
    public void CongratulatorySong_ConsumesAndBroadcastsCashSong()
    {
        var player = CreatePlayerWithCashItem(910000000, 5100000, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5100000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Single(result.MapPackets);
        Assert.Equal(V113ChannelSendOp.CashSong, BitConverter.ToInt16(result.MapPackets[0], 0));
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void Note_SendsViaNoteServiceAndConsumesItem()
    {
        var notes = new TestNoteRepository();
        var player = CreatePlayerWithCashItem(910000000, 5090100, 1);
        var handler = CreateHandler(notes: notes);
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5090100)
            .WriteMapleString("Receiver")
            .WriteMapleString("hello")
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
        var note = Assert.Single(notes.Notes);
        Assert.Equal("CashPlayer", note.SenderName);
        Assert.Equal("Receiver", note.ReceiverName);
        Assert.Equal("hello", note.Message);
    }

    [Fact]
    public void PetName_ChangesActivePetNameAndConsumesItem()
    {
        var pets = new PetService();
        var player = CreatePlayerWithCashItemAndPet(5170000, petFlags: 0);
        Assert.True(pets.SpawnPet(player, cashSlot: 10, lead: true).Success);
        var handler = CreateHandler(pets: pets);
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5170000)
            .WriteMapleString("Buddy")
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Equal("Buddy", pets.GetActivePet(player)!.Name);
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
        Assert.Equal(V113PetPackets.SendPetNameChange, BitConverter.ToInt16(Assert.Single(result.BroadcastPackets), 0));
    }

    [Fact]
    public void PetSkill_AddsFlagAndConsumesItem()
    {
        var pets = new PetService();
        var player = CreatePlayerWithCashItemAndPet(5190000, petFlags: 0);
        Assert.True(pets.SpawnPet(player, cashSlot: 10, lead: true).Success);
        var handler = CreateHandler(pets: pets);
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5190000)
            .WriteLong(1001)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Equal(PetConstants.ItemPickupFlag, pets.GetActivePet(player)!.Flags & PetConstants.ItemPickupFlag);
        Assert.Contains(result.Packets, packet => BitConverter.ToInt16(packet, 0) == V113PetPackets.SendPetFlagChange);
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void PetSkill_RemovesFlagAndConsumesItem()
    {
        var pets = new PetService();
        var player = CreatePlayerWithCashItemAndPet(5191000, PetConstants.ItemPickupFlag);
        Assert.True(pets.SpawnPet(player, cashSlot: 10, lead: true).Success);
        var handler = CreateHandler(pets: pets);
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5191000)
            .WriteLong(1001)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Equal(0, pets.GetActivePet(player)!.Flags & PetConstants.ItemPickupFlag);
        Assert.Contains(result.Packets, packet => BitConverter.ToInt16(packet, 0) == V113PetPackets.SendPetFlagChange);
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void CashPetFood_FeedsActivePetAndConsumesItem()
    {
        var pets = new PetService();
        var player = CreatePlayerWithCashItemAndPet(5240000, petFlags: 0);
        Assert.True(pets.SpawnPet(player, cashSlot: 10, lead: true).Success);
        var handler = CreateHandler(pets: pets);
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5240000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        var pet = pets.GetActivePet(player)!;
        Assert.Equal(100, pet.Fullness);
        Assert.Equal(100, pet.Closeness);
        Assert.Equal(2, result.BroadcastPackets.Count);
        Assert.Contains(result.BroadcastPackets, packet => BitConverter.ToInt16(packet, 0) == V113ChannelSendOp.ShowForeignEffect);
        Assert.Contains(result.BroadcastPackets, packet => BitConverter.ToInt16(packet, 0) == V113PetPackets.SendPetCommand);
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void OpcodeConstant_MatchesJavaValue()
    {
        Assert.Equal(0x49, V113ChannelRecvOp.UseCashItem);
    }

    private static V113UseCashItemHandler CreateHandler(
        PetService? pets = null,
        TestNoteRepository? notes = null)
        => new(
            new OwlService(new EmptyOwlSearchCatalog()),
            pets ?? new PetService(),
            new NoteService(notes ?? new TestNoteRepository()),
            NullLogger<V113UseCashItemHandler>.Instance);

    private static V113UseCashItemHandler CreateHandlerWithResults(
        PetService? pets = null,
        TestNoteRepository? notes = null)
        => new(
            new OwlService(new TestOwlSearchCatalog()),
            pets ?? new PetService(),
            new NoteService(notes ?? new TestNoteRepository()),
            NullLogger<V113UseCashItemHandler>.Instance);

    private sealed class TestOwlSearchCatalog : IOwlSearchCatalog
    {
        public IReadOnlyList<OwlSearchEntry> Search(int itemId)
            => [new OwlSearchEntry("TestShop", 910000000, "Test Item", 1, 1, 100, 1, 0, InventoryType.Etc)];
    }

    private static Player CreateCashOwlPlayer(int mapId)
    {
        var character = new Character
        {
            Id = 1,
            Name = "CashOwl",
            MapId = mapId,
            Items =
            [
                new ItemRecord
                {
                    Type = (byte)InventoryType.Cash,
                    ItemId = OwlService.CashOwlItemId,
                    Slot = 1,
                    Quantity = 1,
                    Expiration = -1,
                },
            ],
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }

    private static Player CreatePlayerWithCashItem(int mapId, int itemId, short slot)
    {
        var character = new Character
        {
            Id = 1,
            Name = "CashPlayer",
            MapId = mapId,
            Items =
            [
                new ItemRecord
                {
                    Type = (byte)InventoryType.Cash,
                    ItemId = itemId,
                    Slot = slot,
                    Quantity = 1,
                    Expiration = -1,
                },
            ],
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }

    private static Player CreatePlayerWithCashItemAndPet(int cashItemId, int petFlags)
    {
        var character = new Character
        {
            Id = 1,
            Name = "CashPlayer",
            MapId = 910000000,
            Items =
            [
                new ItemRecord
                {
                    Type = (byte)InventoryType.Cash,
                    ItemId = cashItemId,
                    Slot = 1,
                    Quantity = 1,
                    Expiration = -1,
                },
                new ItemRecord
                {
                    Type = (byte)InventoryType.Cash,
                    ItemId = 5000000,
                    Slot = 10,
                    Quantity = 1,
                    Owner = "Kitty",
                    Expiration = -1,
                    Flag = (short)petFlags,
                    UniqueId = 1001,
                },
            ],
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }

    private sealed class TestNoteRepository : INoteRepository
    {
        public List<Note> Notes { get; } = new();

        public Task<IReadOnlyList<Note>> GetNotesForCharacterAsync(string name, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Note>>(Notes.Where(note => note.ReceiverName == name).ToArray());

        public Task<Note> AddNoteAsync(Note note, CancellationToken ct = default)
        {
            note.Id = Notes.Count + 1;
            Notes.Add(note);
            return Task.FromResult(note);
        }

        public Task<Note?> DeleteNoteAsync(int id, CancellationToken ct = default)
        {
            var note = Notes.FirstOrDefault(note => note.Id == id);
            if (note is not null)
            {
                Notes.Remove(note);
            }

            return Task.FromResult(note);
        }
    }
}
