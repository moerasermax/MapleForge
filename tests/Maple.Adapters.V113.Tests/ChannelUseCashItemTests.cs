using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Maple.Application.NpcItemServices;
using Maple.Application.Pets;
using Maple.Application.Social;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.NpcItemServices;
using Maple.Core.Pets;
using Maple.Core.Skills;
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
    public void ItemTag_SetsEquippedOwnerAndConsumesItem()
    {
        var player = CreatePlayerWithCashItem(910000000, 5060000, 1);
        player.Character.Equips.Add(new EquipEntry { Position = -1, ItemId = 1002000, Expiration = -1 });
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5060000)
            .WriteByte(-1)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Equal("CashPlayer", player.Character.Equips.Single().Owner);
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
        Assert.Contains(result.Packets, packet => BitConverter.ToInt16(packet, 0) == V113ChannelSendOp.ModifyInventoryItem);
    }

    [Fact]
    public void ItemTag_WithExistingOwner_DoesNotConsume()
    {
        var player = CreatePlayerWithCashItem(910000000, 5060000, 1);
        player.Character.Equips.Add(new EquipEntry { Position = -1, ItemId = 1002000, Owner = "Tagged", Expiration = -1 });
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5060000)
            .WriteByte(-1)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.False(result.CharacterMutated);
        Assert.Equal("Tagged", player.Character.Equips.Single().Owner);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
        Assert.Single(result.Packets);
    }

    [Fact]
    public void SealingLock_Permanent_SetsLockFlagAndConsumesItem()
    {
        var player = CreatePlayerWithCashItemAndItems(5060001, BagEquip(1302000, 2, upgradeSlots: 7));
        var handler = CreateHandler();
        var body = CashItemTargetBody(5060001, InventoryType.Equip, 2);

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.CharacterMutated);
        var equip = player.Inventory.By(InventoryType.Equip).Get(2)!;
        Assert.True(ItemFlags.Has(equip.Flag, ItemFlags.Lock));
        Assert.Equal(-1, equip.Expiration);
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
        Assert.Contains(player.Character.Items, item => item is { Type: (byte)InventoryType.Equip, Slot: 2, Flag: ItemFlags.Lock });
    }

    [Fact]
    public void SealingLock_Timed_SetsExpirationAndConsumesItem()
    {
        var player = CreatePlayerWithCashItemAndItems(5061000, BagItem(InventoryType.Use, 2000000, 2));
        var handler = CreateHandler();
        var before = DateTimeOffset.UtcNow.AddDays(7).AddMinutes(-1).ToUnixTimeMilliseconds();
        var body = CashItemTargetBody(5061000, InventoryType.Use, 2);

        var result = handler.Handle(new PacketReader(body), player);

        var after = DateTimeOffset.UtcNow.AddDays(7).AddMinutes(1).ToUnixTimeMilliseconds();
        Assert.True(result.CharacterMutated);
        var item = player.Inventory.By(InventoryType.Use).Get(2)!;
        Assert.True(ItemFlags.Has(item.Flag, ItemFlags.Lock));
        Assert.InRange(item.Expiration, before, after);
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void SealingLock_WithExistingExpiration_DoesNotConsume()
    {
        var player = CreatePlayerWithCashItemAndItems(
            5061001,
            BagItem(InventoryType.Use, 2000000, 2, expiration: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        var handler = CreateHandler();
        var body = CashItemTargetBody(5061001, InventoryType.Use, 2);

        var result = handler.Handle(new PacketReader(body), player);

        Assert.False(result.CharacterMutated);
        Assert.Equal(0, player.Inventory.By(InventoryType.Use).Get(2)!.Flag);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void Karma_OnEquip_SetsEquipKarmaFlagAndConsumesItem()
    {
        var player = CreatePlayerWithCashItemAndItems(5520000, BagEquip(1302000, 2, upgradeSlots: 7));
        var handler = CreateHandler();
        var body = CashItemTargetBody(5520000, InventoryType.Equip, 2);

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.CharacterMutated);
        Assert.True(ItemFlags.Has(player.Inventory.By(InventoryType.Equip).Get(2)!.Flag, ItemFlags.KarmaEquip));
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void Karma_OnUseItem_SetsUseKarmaFlagAndConsumesItem()
    {
        var player = CreatePlayerWithCashItemAndItems(5520001, BagItem(InventoryType.Use, 2040000, 2));
        var handler = CreateHandler();
        var body = CashItemTargetBody(5520001, InventoryType.Use, 2);

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.CharacterMutated);
        Assert.True(ItemFlags.Has(player.Inventory.By(InventoryType.Use).Get(2)!.Flag, ItemFlags.KarmaUse));
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void Karma_WhenAlreadyFlagged_DoesNotConsume()
    {
        var player = CreatePlayerWithCashItemAndItems(
            5520000,
            BagEquip(1302000, 2, upgradeSlots: 7, flag: ItemFlags.KarmaEquip));
        var handler = CreateHandler();
        var body = CashItemTargetBody(5520000, InventoryType.Equip, 2);

        var result = handler.Handle(new PacketReader(body), player);

        Assert.False(result.CharacterMutated);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void SpReset_TransfersOnePointAndConsumesItem()
    {
        var player = CreatePlayerWithCashItem(910000000, 5050002, 1);
        player.Character.Skills.Add(new CharacterSkillRecord { SkillId = 2001002, Level = 5, MasterLevel = 10 });
        player.Character.Skills.Add(new CharacterSkillRecord { SkillId = 2001003, Level = 1, MasterLevel = 10 });
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5050002)
            .WriteInt(2001003)
            .WriteInt(2001002)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(4, player.GetSkillLevel(2001002));
        Assert.Equal(2, player.GetSkillLevel(2001003));
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
        Assert.Equal(2, result.Packets.Count(packet => BitConverter.ToInt16(packet, 0) == V113StatsPackets.SendUpdateSkills));
    }

    [Fact]
    public void SpReset_WithBeginnerSkill_DoesNotConsume()
    {
        var player = CreatePlayerWithCashItem(910000000, 5050001, 1);
        player.Character.Skills.Add(new CharacterSkillRecord { SkillId = 2001002, Level = 5, MasterLevel = 10 });
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5050001)
            .WriteInt(1000)
            .WriteInt(2001002)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.False(result.CharacterMutated);
        Assert.Equal(5, player.GetSkillLevel(2001002));
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void ApReset_TransfersBasicStatAndConsumesItem()
    {
        var player = CreatePlayerWithCashItem(910000000, 5050000, 1);
        player.Character.Stats.Str = 10;
        player.Character.Stats.Dex = 5;
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5050000)
            .WriteInt(0x80)
            .WriteInt(0x40)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(9, player.Character.Stats.Str);
        Assert.Equal(6, player.Character.Stats.Dex);
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
        Assert.Contains(result.Packets, packet => BitConverter.ToInt16(packet, 0) == V113StatsPackets.SendUpdateStats);
    }

    [Fact]
    public void ApReset_HpMpPathIsDeferredAndDoesNotConsume()
    {
        var player = CreatePlayerWithCashItem(910000000, 5050000, 1);
        player.Character.Stats.Str = 10;
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5050000)
            .WriteInt(0x800)
            .WriteInt(0x40)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.False(result.CharacterMutated);
        Assert.Equal(10, player.Character.Stats.Str);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void ViciousHammer_IncrementsHammerAndSlotsAndConsumesItem()
    {
        var player = CreatePlayerWithCashItemAndItems(5570000, BagEquip(1302000, 2, upgradeSlots: 1));
        var handler = CreateHandler();
        var body = CashItemTargetBody(5570000, InventoryType.Equip, 2);

        var result = handler.Handle(new PacketReader(body), player);

        var equip = Assert.IsType<Equip>(player.Inventory.By(InventoryType.Equip).Get(2));
        Assert.True(result.CharacterMutated);
        Assert.Equal(1, equip.ViciousHammer);
        Assert.Equal(2, equip.UpgradeSlots);
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void ViciousHammer_WithNoSlots_DoesNotConsume()
    {
        var player = CreatePlayerWithCashItemAndItems(5570000, BagEquip(1302000, 2, upgradeSlots: 0));
        var handler = CreateHandler();
        var body = CashItemTargetBody(5570000, InventoryType.Equip, 2);

        var result = handler.Handle(new PacketReader(body), player);

        var equip = Assert.IsType<Equip>(player.Inventory.By(InventoryType.Equip).Get(2));
        Assert.False(result.CharacterMutated);
        Assert.Equal(0, equip.ViciousHammer);
        Assert.Equal(0, equip.UpgradeSlots);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void VegaScroll_IsDeferredAndDoesNotConsume()
    {
        var player = CreatePlayerWithCashItem(910000000, 5610000, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5610000)
            .WriteInt((int)InventoryType.Equip)
            .WriteInt(2)
            .WriteInt((int)InventoryType.Use)
            .WriteInt(3)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.False(result.CharacterMutated);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
        Assert.Single(result.Packets);
    }

    [Fact]
    public void FixedDestinationTeleport_5042000_WarpsToYuyuanAndConsumes()
    {
        var player = CreatePlayerWithCashItem(910000000, 5042000, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5042000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        AssertWarpConsumed(result, player, expectedMapId: 701000200);
    }

    [Fact]
    public void FixedDestinationTeleport_5042001_WarpsToNightMarketAndConsumes()
    {
        var player = CreatePlayerWithCashItem(910000000, 5042001, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5042001)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        AssertWarpConsumed(result, player, expectedMapId: 741000000);
    }

    [Theory]
    [InlineData(5040000)]
    [InlineData(5040001)]
    [InlineData(5041000)]
    [InlineData(2320000)]
    public void TeleportRock_MapMode_ReadsMapIdAndConsumes(int itemId)
    {
        var player = CreatePlayerWithCashItem(910000000, itemId, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(itemId)
            .WriteByte(0)
            .WriteInt(100000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        AssertWarpConsumed(result, player, expectedMapId: 100000000);
    }

    [Fact]
    public void TeleportRock_MapMode_TargetMapHasVipRockFieldLimit_ReturnsEnableActionsAndDoesNotConsume()
    {
        var player = CreatePlayerWithCashItem(910000000, 5040000, 1);
        var maps = new MapService(new FakeMapFieldLimitProvider(new Dictionary<int, long> { [100000000] = 0x40 }));
        var handler = CreateHandler(maps: maps);
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5040000)
            .WriteByte(0)
            .WriteInt(100000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Null(result.WarpToMapId);
        Assert.Single(result.Packets);
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[0], 0));
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void TeleportRock_MapMode_CurrentMapHasVipRockFieldLimit_ReturnsEnableActionsAndDoesNotConsume()
    {
        var player = CreatePlayerWithCashItem(910000000, 5040000, 1);
        var maps = new MapService(new FakeMapFieldLimitProvider(new Dictionary<int, long> { [910000000] = 0x40 }));
        var handler = CreateHandler(maps: maps);
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5040000)
            .WriteByte(0)
            .WriteInt(100000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Null(result.WarpToMapId);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void TeleportRock_PlayerMode_IsDeferredAndDoesNotConsume()
    {
        var player = CreatePlayerWithCashItem(910000000, 5040000, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5040000)
            .WriteByte(1)
            .WriteMapleString("Victim")
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Null(result.WarpToMapId);
        Assert.Single(result.Packets);
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[0], 0));
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Theory]
    [InlineData(5560000)]
    [InlineData(5561000)]
    public void AnyDoor_MapMode_ReadsMapIdAndConsumes(int itemId)
    {
        var player = CreatePlayerWithCashItem(910000000, itemId, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(itemId)
            .WriteByte(0)
            .WriteInt(200000001)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        AssertWarpConsumed(result, player, expectedMapId: 200000001);
    }

    [Fact]
    public void AnyDoor_MapMode_TargetMapHasVipRockFieldLimit_ReturnsEnableActionsAndDoesNotConsume()
    {
        var player = CreatePlayerWithCashItem(910000000, 5560000, 1);
        var maps = new MapService(new FakeMapFieldLimitProvider(new Dictionary<int, long> { [200000001] = 0x40 }));
        var handler = CreateHandler(maps: maps);
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5560000)
            .WriteByte(0)
            .WriteInt(200000001)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Null(result.WarpToMapId);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void AnyDoor_NonMapMode_ReturnsEnableActionsAndDoesNotConsume()
    {
        var player = CreatePlayerWithCashItem(910000000, 5560000, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5560000)
            .WriteByte(1)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Null(result.WarpToMapId);
        Assert.Single(result.Packets);
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[0], 0));
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void MapMegaphone_ConsumesAndBroadcastsServerMessageType2()
    {
        var player = CreatePlayerWithCashItem(910000000, 5070000, 1);
        var handler = CreateHandler();
        var body = CashMegaphoneBody(5070000, "hello map", ear: true);

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
        var reader = ReadServerMessage(Assert.Single(result.MapPackets), expectedType: 2);
        Assert.Equal("CashPlayer : hello map", reader.ReadMapleString());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void Megaphone_LevelTooLow_DoesNotConsumeOrBroadcast()
    {
        var player = CreatePlayerWithCashItem(910000000, 5071000, 1, level: 9);
        var handler = CreateHandler();
        var body = CashMegaphoneBody(5071000, "too low", ear: true);

        var result = handler.Handle(new PacketReader(body), player, channel: 3);

        Assert.False(result.CharacterMutated);
        Assert.Empty(result.ChannelBroadcastPackets);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
        Assert.Single(result.Packets);
    }

    [Fact]
    public void Megaphone_MessageTooLong_DoesNotConsumeOrBroadcast()
    {
        var player = CreatePlayerWithCashItem(910000000, 5071000, 1);
        var handler = CreateHandler();
        var body = CashMegaphoneBody(5071000, new string('x', 66), ear: true);

        var result = handler.Handle(new PacketReader(body), player, channel: 3);

        Assert.False(result.CharacterMutated);
        Assert.Empty(result.ChannelBroadcastPackets);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void ChannelMegaphone5071000_WritesPlainMegaphoneFormat_ChannelWide()
    {
        // 對照 Java 5071000 分支：讀 message+ear，但建包只呼叫 getMegaphone(message)——ear 是
        // Java 原始碼本身的死變數，plain Megaphone 格式沒有 channel/ear 欄位；範圍是
        // c.getChannelServer().broadcastSmega（頻道範圍，非地圖）。
        var player = CreatePlayerWithCashItem(910000000, 5071000, 1);
        var handler = CreateHandler();
        var body = CashMegaphoneBody(5071000, "hello channel", ear: true);

        var result = handler.Handle(new PacketReader(body), player, channel: 5);

        Assert.Empty(result.MapPackets);
        var reader = ReadServerMessage(Assert.Single(result.ChannelBroadcastPackets), expectedType: 2);
        Assert.Equal("CashPlayer : hello channel", reader.ReadMapleString());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void SuperMegaphone5072000_WritesChannelAndEar_WorldWide()
    {
        // 對照 Java 5072000（高效能喇叭）：World.Broadcast.broadcastSmega（全服）+ getSuperMegaphone 格式。
        var player = CreatePlayerWithCashItem(910000000, 5072000, 1);
        var handler = CreateHandler();
        var body = CashMegaphoneBody(5072000, "hello world", ear: true);

        var result = handler.Handle(new PacketReader(body), player, channel: 5);

        Assert.Empty(result.MapPackets);
        var reader = ReadServerMessage(Assert.Single(result.ChannelBroadcastPackets), expectedType: 3);
        Assert.Equal("CashPlayer : hello world", reader.ReadMapleString());
        Assert.Equal(4, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());
    }

    [Theory]
    [InlineData(5073000, 11)]
    [InlineData(5074000, 12)]
    public void StyledMegaphones_WriteExpectedType(int itemId, int expectedType)
    {
        var player = CreatePlayerWithCashItem(910000000, itemId, 1);
        var handler = CreateHandler();
        var body = CashMegaphoneBody(itemId, "styled", ear: false);

        var result = handler.Handle(new PacketReader(body), player, channel: 2);

        Assert.Empty(result.MapPackets);
        var reader = ReadServerMessage(Assert.Single(result.ChannelBroadcastPackets), expectedType);
        Assert.Equal("CashPlayer : styled", reader.ReadMapleString());
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(0, reader.ReadByte());
    }

    [Fact]
    public void MapleTvStub_OnlyEnablesActionsWithoutConsuming()
    {
        var player = CreatePlayerWithCashItem(910000000, 5075000, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5075000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.False(result.CharacterMutated);
        Assert.Empty(result.MapPackets);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(Assert.Single(result.Packets), 0));
    }

    [Fact]
    public void ItemMegaphone_WithoutItem_WritesNoItemFlag()
    {
        var player = CreatePlayerWithCashItem(910000000, 5076000, 1);
        var handler = CreateHandler();
        var body = CashItemMegaphoneBody("selling", ear: false, includeItem: false);

        var result = handler.Handle(new PacketReader(body), player, channel: 4);

        Assert.Empty(result.MapPackets);
        var reader = ReadServerMessage(Assert.Single(result.ChannelBroadcastPackets), expectedType: 8);
        Assert.Equal("CashPlayer : selling", reader.ReadMapleString());
        Assert.Equal(3, reader.ReadByte());
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ItemMegaphone_WithItem_WritesItemInfo()
    {
        var player = CreatePlayerWithCashItemAndItems(
            5076000,
            BagItem(InventoryType.Use, 2000000, 2));
        var handler = CreateHandler();
        var body = CashItemMegaphoneBody("selling item", ear: true, includeItem: true);

        var result = handler.Handle(new PacketReader(body), player, channel: 4);

        Assert.Empty(result.MapPackets);
        var reader = ReadServerMessage(Assert.Single(result.ChannelBroadcastPackets), expectedType: 8);
        Assert.Equal("CashPlayer : selling item", reader.ReadMapleString());
        Assert.Equal(3, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(2, reader.ReadByte());
        Assert.Equal(2000000, reader.ReadInt());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TripleMegaphone_WritesRequestedLineCount(int lineCount)
    {
        var player = CreatePlayerWithCashItem(910000000, 5077000, 1);
        var handler = CreateHandler();
        var messages = Enumerable.Range(1, lineCount).Select(i => $"line {i}").ToArray();
        var body = CashTripleMegaphoneBody(messages, ear: true);

        var result = handler.Handle(new PacketReader(body), player, channel: 2);

        Assert.Empty(result.MapPackets);
        var reader = ReadServerMessage(Assert.Single(result.ChannelBroadcastPackets), expectedType: 10);
        Assert.Equal("CashPlayer : line 1", reader.ReadMapleString());
        Assert.Equal(lineCount, reader.ReadByte());
        if (lineCount > 1)
        {
            Assert.Equal("CashPlayer : line 2", reader.ReadMapleString());
        }

        if (lineCount > 2)
        {
            Assert.Equal("CashPlayer : line 3", reader.ReadMapleString());
        }

        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());
    }

    [Fact]
    public void TripleMegaphone_TooManyLines_DoesNotConsume()
    {
        var player = CreatePlayerWithCashItem(910000000, 5077000, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5077000)
            .WriteByte(4)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player, channel: 2);

        Assert.False(result.CharacterMutated);
        Assert.Empty(result.ChannelBroadcastPackets);
        Assert.Equal(1, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void AvatarMegaphone_FallsBackToSuperMegaphone()
    {
        var player = CreatePlayerWithCashItem(910000000, 5390029, 1);
        var handler = CreateHandler();
        var body = CashMegaphoneBody(5390029, "avatar", ear: true);

        var result = handler.Handle(new PacketReader(body), player, channel: 6);

        Assert.Empty(result.MapPackets);
        var reader = ReadServerMessage(Assert.Single(result.ChannelBroadcastPackets), expectedType: 3);
        Assert.Equal("CashPlayer : avatar", reader.ReadMapleString());
        Assert.Equal(5, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

    [Fact]
    public void OpcodeConstant_MatchesJavaValue()
    {
        Assert.Equal(0x49, V113ChannelRecvOp.UseCashItem);
    }

    private static V113UseCashItemHandler CreateHandler(
        PetService? pets = null,
        TestNoteRepository? notes = null,
        MapService? maps = null)
        => new(
            new OwlService(new EmptyOwlSearchCatalog()),
            pets ?? new PetService(),
            new NoteService(notes ?? new TestNoteRepository()),
            maps ?? new MapService(new FakeMapFieldLimitProvider()),
            NullLogger<V113UseCashItemHandler>.Instance);

    private static V113UseCashItemHandler CreateHandlerWithResults(
        PetService? pets = null,
        TestNoteRepository? notes = null)
        => new(
            new OwlService(new TestOwlSearchCatalog()),
            pets ?? new PetService(),
            new NoteService(notes ?? new TestNoteRepository()),
            new MapService(new FakeMapFieldLimitProvider()),
            NullLogger<V113UseCashItemHandler>.Instance);

    /// <summary>P041：讓測試可以指定特定 mapId 的 <c>info/fieldLimit</c>，其餘 mapId 一律回傳 0（不受限）。</summary>
    private sealed class FakeMapFieldLimitProvider : IDataProvider
    {
        private readonly Dictionary<int, long> _fieldLimits;

        public FakeMapFieldLimitProvider(Dictionary<int, long>? fieldLimits = null)
            => _fieldLimits = fieldLimits ?? new Dictionary<int, long>();

        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path)
        {
            if (fileName != "Map")
            {
                return null;
            }

            var fileNamePart = path.Split('/')[^1];
            var mapId = int.Parse(fileNamePart.AsSpan(0, fileNamePart.IndexOf('.')));
            var infoChildren = new Dictionary<string, IDataNode>();
            if (_fieldLimits.TryGetValue(mapId, out var fieldLimit))
            {
                infoChildren["fieldLimit"] = new Node("fieldLimit", (int)fieldLimit);
            }

            return new Node(fileNamePart, children: new Dictionary<string, IDataNode>
            {
                ["info"] = new Node("info", children: infoChildren),
                ["portal"] = new Node("portal"),
                ["foothold"] = new Node("foothold"),
                ["life"] = new Node("life"),
            });
        }

        private sealed class Node : IDataNode
        {
            public Node(string name, object? value = null, IReadOnlyDictionary<string, IDataNode>? children = null)
            {
                Name = name;
                Value = value;
                Children = children ?? new Dictionary<string, IDataNode>();
            }

            public string Name { get; }

            public IReadOnlyDictionary<string, IDataNode> Children { get; }

            public object? Value { get; }

            public IDataNode? this[string name] => Children.TryGetValue(name, out var child) ? child : null;
        }
    }

    private static void AssertWarpConsumed(V113UseCashItemResult result, Player player, int expectedMapId)
    {
        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Equal(expectedMapId, result.WarpToMapId);
        Assert.Equal(2, result.Packets.Count);
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.Packets[0], 0));
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[1], 0));
        Assert.Equal(0, player.Inventory.By(InventoryType.Cash).Get(1)!.Quantity);
    }

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

    private static Player CreatePlayerWithCashItem(int mapId, int itemId, short slot, byte level = 10)
    {
        var character = new Character
        {
            Id = 1,
            Name = "CashPlayer",
            Level = level,
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
            Level = 10,
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

    private static Player CreatePlayerWithCashItemAndItems(int cashItemId, params ItemRecord[] items)
    {
        var records = new List<ItemRecord>
        {
            new()
            {
                Type = (byte)InventoryType.Cash,
                ItemId = cashItemId,
                Slot = 1,
                Quantity = 1,
                Expiration = -1,
            },
        };
        records.AddRange(items);

        var character = new Character
        {
            Id = 1,
            Name = "CashPlayer",
            Level = 10,
            MapId = 910000000,
            Items = records,
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }

    private static byte[] CashItemTargetBody(int cashItemId, InventoryType type, short targetSlot)
        => new PacketWriter()
            .WriteShort(1)
            .WriteInt(cashItemId)
            .WriteInt((int)type)
            .WriteInt(targetSlot)
            .ToArray();

    private static byte[] CashMegaphoneBody(int cashItemId, string message, bool ear)
        => new PacketWriter()
            .WriteShort(1)
            .WriteInt(cashItemId)
            .WriteMapleString(message)
            .WriteByte(ear ? 1 : 0)
            .ToArray();

    private static byte[] CashItemMegaphoneBody(string message, bool ear, bool includeItem)
    {
        var writer = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5076000)
            .WriteMapleString(message)
            .WriteByte(ear ? 1 : 0)
            .WriteByte(includeItem ? 1 : 0);

        if (includeItem)
        {
            writer.WriteInt((int)InventoryType.Use);
            writer.WriteInt(2);
        }

        return writer.ToArray();
    }

    private static byte[] CashTripleMegaphoneBody(IReadOnlyList<string> messages, bool ear)
    {
        var writer = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5077000)
            .WriteByte(messages.Count);

        foreach (var message in messages)
        {
            writer.WriteMapleString(message);
        }

        writer.WriteByte(ear ? 1 : 0);
        return writer.ToArray();
    }

    private static PacketReader ReadServerMessage(byte[] packet, int expectedType)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113ChannelSendOp.ServerMessage, reader.ReadShort());
        Assert.Equal((byte)expectedType, reader.ReadByte());
        return reader;
    }

    private static ItemRecord BagItem(
        InventoryType type,
        int itemId,
        short slot,
        short flag = 0,
        long expiration = -1)
        => new()
        {
            Type = (byte)type,
            ItemId = itemId,
            Slot = slot,
            Quantity = 1,
            Expiration = expiration,
            Flag = flag,
        };

    private static ItemRecord BagEquip(
        int itemId,
        short slot,
        byte upgradeSlots,
        short flag = 0,
        long expiration = -1,
        byte viciousHammer = 0)
        => new()
        {
            Type = (byte)InventoryType.Equip,
            IsEquip = true,
            ItemId = itemId,
            Slot = slot,
            Quantity = 1,
            Expiration = expiration,
            Flag = flag,
            UpgradeSlots = upgradeSlots,
            ViciousHammer = viciousHammer,
        };

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
