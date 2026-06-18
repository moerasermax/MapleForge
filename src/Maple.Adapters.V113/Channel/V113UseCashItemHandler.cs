using Maple.Application.Pets;
using Maple.Application.NpcItemServices;
using Maple.Application.Social;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Pets;
using Maple.Core.Shops;
using Maple.Core.World;
using Microsoft.Extensions.Logging;

namespace Maple.Adapters.V113.Channel;

/// <summary>Result of handling USE_CASH_ITEM (0x49).</summary>
internal sealed record V113UseCashItemResult(
    bool Handled,
    bool CharacterMutated,
    IReadOnlyList<byte[]> Packets)
{
    public IReadOnlyList<byte[]> BroadcastPackets { get; init; } = Array.Empty<byte[]>();

    public IReadOnlyList<byte[]> MapPackets { get; init; } = Array.Empty<byte[]>();
}

/// <summary>
/// v113 USE_CASH_ITEM (0x49) handler. Cash items used from the Cash inventory tab
/// dispatch through this opcode with a switch on itemId.
/// Currently routes 5230000 (Owl of Minerva cash) to <see cref="OwlService"/>.
/// </summary>
public sealed class V113UseCashItemHandler
{
    private readonly OwlService _owlService;
    private readonly PetService _petService;
    private readonly NoteService _noteService;
    private readonly ILogger<V113UseCashItemHandler> _log;

    public V113UseCashItemHandler(
        OwlService owlService,
        PetService petService,
        NoteService noteService,
        ILogger<V113UseCashItemHandler> log)
    {
        _owlService = owlService;
        _petService = petService;
        _noteService = noteService;
        _log = log;
    }

    internal V113UseCashItemResult Handle(PacketReader reader, Player player)
        => HandleAsync(reader, player, CancellationToken.None).GetAwaiter().GetResult();

    internal async Task<V113UseCashItemResult> HandleAsync(
        PacketReader reader,
        Player player,
        CancellationToken ct = default)
    {
        if (reader.Remaining < 6)
        {
            return EnableActionsOnly();
        }

        short slot = reader.ReadShort();
        int itemId = reader.ReadInt();

        // Validate: player has the item in Cash inventory at that slot with matching itemId
        var cashBag = player.Inventory.By(InventoryType.Cash);
        var item = cashBag.Get(slot);
        if (item is null || item.ItemId != itemId || item.Quantity <= 0)
        {
            _log.LogDebug("[UseCashItem] Item validation failed slot={Slot} itemId={ItemId}", slot, itemId);
            return EnableActionsOnly();
        }

        return itemId switch
        {
            OwlService.CashOwlItemId => HandleOwlOfMinerva(reader, player, slot, itemId),
            5050000 => HandleApReset(reader, player, slot, itemId),
            >= 5050001 and <= 5050004 => HandleSpReset(reader, player, slot, itemId),
            5060000 => HandleItemTag(reader, player, slot, itemId),
            5060001 => HandleSealingLock(reader, player, slot, itemId, lockDays: 0),
            5060003 => HandleStub(itemId, "Peanut incubator reward"),
            5060005 => HandleStub(itemId, "Incubator unavailable"),
            5061000 => HandleSealingLock(reader, player, slot, itemId, lockDays: 7),
            5061001 => HandleSealingLock(reader, player, slot, itemId, lockDays: 30),
            5061002 => HandleSealingLock(reader, player, slot, itemId, lockDays: 90),
            5090000 or 5090100 => await HandleNoteAsync(reader, player, slot, itemId, ct).ConfigureAwait(false),
            5100000 => HandleCongratulatorySong(player, slot, itemId),
            >= 5152100 and <= 5152107 => HandleStub(itemId, "Contact lenses"),
            5170000 => HandlePetName(reader, player, slot, itemId),
            >= 5190000 and <= 5190008 => HandlePetSkill(reader, player, slot, itemId),
            >= 5191000 and <= 5191004 => HandlePetSkill(reader, player, slot, itemId),
            >= 5201001 and <= 5201003 => HandleStub(itemId, "Steel ball box"),
            >= 5240000 and <= 5240028 => HandleCashPetFood(player, slot, itemId),
            5280001 or 5281000 or 5281001 => HandleStub(itemId, "Gas or flower map effect"),
            5320000 => HandleStub(itemId, "Predict card"),
            5330000 => HandleStub(itemId, "NPC script cash item"),
            5370000 or 5370001 => HandleChalkboard(reader, player),
            5450000 => HandleStub(itemId, "Travelling merchant"),
            5520000 or 5520001 => HandleKarma(reader, player, slot, itemId),
            5570000 => HandleViciousHammer(reader, player, slot, itemId),
            5610000 or 5610001 => HandleVegaStub(reader, itemId),
            _ => HandleUnknown(itemId),
        };
    }

    private V113UseCashItemResult HandleApReset(PacketReader reader, Player player, short slot, int itemId)
    {
        if (reader.Remaining < 8)
        {
            return EnableActionsOnly();
        }

        var rawTo = reader.ReadInt();
        var rawFrom = reader.ReadInt();
        var to = MapBasicAbilityPointTarget(rawTo);
        var from = MapBasicAbilityPointTarget(rawFrom);
        if (to is null || from is null)
        {
            // TODO: HP/MP reset needs Java's job-specific HP/MP formulas and HPMP AP-use tracking.
            _log.LogDebug(
                "[UseCashItem] AP Reset HP/MP or unsupported stat deferred itemId={ItemId} to=0x{To:X} from=0x{From:X}",
                itemId,
                rawTo,
                rawFrom);
            return EnableActionsOnly();
        }

        var mutation = player.ResetBaseAbilityPoint(to.Value, from.Value);
        if (!mutation.Applied)
        {
            return EnableActionsOnly();
        }

        return ConsumeCashItemWithPackets(
            player,
            slot,
            itemId,
            [V113StatsPackets.UpdateStats(mutation.Updates)]);
    }

    private V113UseCashItemResult HandleSpReset(PacketReader reader, Player player, short slot, int itemId)
    {
        if (reader.Remaining < 8)
        {
            return EnableActionsOnly();
        }

        var toSkillId = reader.ReadInt();
        var fromSkillId = reader.ReadInt();
        var mutation = player.ResetSkillPoint(toSkillId, fromSkillId);
        if (!mutation.Applied)
        {
            return EnableActionsOnly();
        }

        return ConsumeCashItemWithPackets(
            player,
            slot,
            itemId,
            mutation.UpdatedSkills.Select(V113SkillPackets.UpdateSkill).ToArray());
    }

    private V113UseCashItemResult HandleItemTag(PacketReader reader, Player player, short slot, int itemId)
    {
        if (reader.Remaining < 1)
        {
            return EnableActionsOnly();
        }

        var equippedSlot = (short)(sbyte)reader.ReadByte();
        var equipped = player.Character.Equips.FirstOrDefault(e => e.Position == equippedSlot);
        if (equipped is null || !string.IsNullOrEmpty(equipped.Owner))
        {
            return EnableActionsOnly();
        }

        equipped.Owner = player.Character.Name;
        return ConsumeCashItemWithPackets(
            player,
            slot,
            itemId,
            [V113InventoryPackets.ModifyItemUpdate(InventoryType.Equip, equippedSlot, equipped.ToEquip())]);
    }

    private V113UseCashItemResult HandleSealingLock(
        PacketReader reader,
        Player player,
        short slot,
        int itemId,
        int lockDays)
    {
        if (!TryReadInventoryTarget(reader, out var type, out var targetSlot))
        {
            return EnableActionsOnly();
        }

        var item = player.Inventory.By(type).Get(targetSlot);
        if (item is null || item.Expiration != -1)
        {
            return EnableActionsOnly();
        }

        item.Flag = ItemFlags.Set(item.Flag, ItemFlags.Lock);
        if (lockDays > 0)
        {
            item.Expiration = DateTimeOffset.UtcNow.AddDays(lockDays).ToUnixTimeMilliseconds();
        }

        return ConsumeCashItemWithPackets(
            player,
            slot,
            itemId,
            [V113InventoryPackets.ModifyItemUpdate(type, targetSlot, item)]);
    }

    private V113UseCashItemResult HandleKarma(PacketReader reader, Player player, short slot, int itemId)
    {
        if (!TryReadInventoryTarget(reader, out var type, out var targetSlot))
        {
            return EnableActionsOnly();
        }

        var item = player.Inventory.By(type).Get(targetSlot);
        if (item is null ||
            ItemFlags.Has(item.Flag, ItemFlags.KarmaEquip) ||
            ItemFlags.Has(item.Flag, ItemFlags.KarmaUse))
        {
            return EnableActionsOnly();
        }

        item.Flag = ItemFlags.Set(item.Flag, item.IsEquip ? ItemFlags.KarmaEquip : ItemFlags.KarmaUse);
        return ConsumeCashItemWithPackets(
            player,
            slot,
            itemId,
            [V113InventoryPackets.ModifyItemUpdate(type, targetSlot, item)]);
    }

    private V113UseCashItemResult HandleViciousHammer(PacketReader reader, Player player, short slot, int itemId)
    {
        if (!TryReadInventoryTarget(reader, out var type, out var targetSlot) || type != InventoryType.Equip)
        {
            return EnableActionsOnly();
        }

        if (player.Inventory.By(InventoryType.Equip).Get(targetSlot) is not Equip equip ||
            !CanHammer(equip) ||
            equip.UpgradeSlots == 0 ||
            equip.ViciousHammer > 2)
        {
            return EnableActionsOnly();
        }

        equip.ViciousHammer++;
        equip.UpgradeSlots++;
        return ConsumeCashItemWithPackets(
            player,
            slot,
            itemId,
            [V113InventoryPackets.ModifyItemUpdate(InventoryType.Equip, targetSlot, equip)]);
    }

    private V113UseCashItemResult HandleVegaStub(PacketReader reader, int itemId)
    {
        if (reader.Remaining >= 16)
        {
            var equipType = reader.ReadInt();
            var equipSlot = ReadSignedByteFromInt(reader);
            var scrollType = reader.ReadInt();
            var scrollSlot = ReadSignedByteFromInt(reader);
            _log.LogDebug(
                "[UseCashItem] Vega scroll deferred itemId={ItemId} equipType={EquipType} equipSlot={EquipSlot} scrollType={ScrollType} scrollSlot={ScrollSlot}",
                itemId,
                equipType,
                equipSlot,
                scrollType,
                scrollSlot);
        }
        else
        {
            _log.LogDebug("[UseCashItem] Vega scroll deferred itemId={ItemId} with short payload", itemId);
        }

        return EnableActionsOnly();
    }

    private V113UseCashItemResult HandleOwlOfMinerva(PacketReader reader, Player player, short slot, int itemId)
    {
        if (reader.Remaining < 4)
        {
            return EnableActionsOnly();
        }

        int searchItemId = reader.ReadInt();

        var searchResult = _owlService.Search(player, searchItemId);
        if (!searchResult.Success || searchResult.Entries.Count == 0)
        {
            return EnableActionsOnly();
        }

        var packets = new List<byte[]>();

        // Java order: send search results first, then consume
        packets.Add(V113OwlPackets.OwlSearched(searchItemId, searchResult.Entries));

        bool consumed = player.TryTakeItemFromSlot(InventoryType.Cash, slot, itemId, 1, out var mutation);
        if (consumed)
        {
            player.FlushInventory();
            packets.Add(V113ShopPackets.ModifyInventoryQuantity(mutation!));
        }

        packets.Add(V113StatsPackets.EnableActions());

        return new V113UseCashItemResult(true, consumed, packets);
    }

    private async Task<V113UseCashItemResult> HandleNoteAsync(
        PacketReader reader,
        Player player,
        short slot,
        int itemId,
        CancellationToken ct)
    {
        string receiver;
        string message;
        try
        {
            receiver = reader.ReadMapleString();
            message = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return EnableActionsOnly();
        }

        var sendResult = await _noteService
            .SendNoteAsync(player.Character.Name, receiver, message, fame: false, ct)
            .ConfigureAwait(false);

        if (!sendResult.Success)
        {
            return EnableActionsOnly();
        }

        return ConsumeCashItem(player, slot, itemId);
    }

    private V113UseCashItemResult HandleCongratulatorySong(Player player, short slot, int itemId)
    {
        var result = ConsumeCashItem(player, slot, itemId);
        if (!result.CharacterMutated)
        {
            return result;
        }

        return result with
        {
            MapPackets = One(V113CashItemPackets.PlayCashSong(itemId, player.Character.Name)),
        };
    }

    private V113UseCashItemResult HandlePetName(PacketReader reader, Player player, short slot, int itemId)
    {
        string name;
        try
        {
            name = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return EnableActionsOnly();
        }

        var petResult = _petService.RenameActivePet(player, name);
        if (!petResult.Success || petResult.Pet is null)
        {
            return EnableActionsOnly();
        }

        var result = ConsumeCashItem(player, slot, itemId);
        if (!result.CharacterMutated)
        {
            return result;
        }

        var selfPackets = new List<byte[]>
        {
            V113PetPackets.UpdatePet(petResult.Pet, petResult.CashSlot),
        };
        selfPackets.AddRange(result.Packets);

        return result with
        {
            Packets = selfPackets,
            BroadcastPackets = One(V113PetPackets.PetNameChanged(player.Character.Id, petResult.Slot, petResult.Pet.Name)),
        };
    }

    private V113UseCashItemResult HandlePetSkill(PacketReader reader, Player player, short slot, int itemId)
    {
        if (reader.Remaining < 8)
        {
            return EnableActionsOnly();
        }

        var uniqueId = ReadLong(reader);
        var isAdd = (itemId / 1000) % 10 == 0;
        var flag = GetPetFlagValue(itemId);
        if (flag <= 0)
        {
            return EnableActionsOnly();
        }

        var petResult = _petService.ChangeActivePetFlag(player, uniqueId, flag, isAdd);
        if (!petResult.Success || petResult.Pet is null)
        {
            return EnableActionsOnly();
        }

        var result = ConsumeCashItem(player, slot, itemId);
        if (!result.CharacterMutated)
        {
            return result;
        }

        var selfPackets = new List<byte[]>
        {
            V113PetPackets.UpdatePet(petResult.Pet, petResult.CashSlot),
            V113PetPackets.PetFlagChanged(uniqueId, isAdd, flag),
        };
        selfPackets.AddRange(result.Packets);

        return result with { Packets = selfPackets };
    }

    private V113UseCashItemResult HandleCashPetFood(Player player, short slot, int itemId)
    {
        var petResult = _petService.FeedActivePetToFull(player, closenessGain: 100);
        if (!petResult.Success || petResult.Pet is null)
        {
            return EnableActionsOnly();
        }

        var result = ConsumeCashItem(player, slot, itemId);
        if (!result.CharacterMutated)
        {
            return result;
        }

        var selfPackets = new List<byte[]>();
        var broadcastPackets = new List<byte[]>();
        if (petResult.Growth.LevelChanged && petResult.Pet.Level > 1)
        {
            selfPackets.Add(V113PetPackets.ShowOwnPetLevelUp(petResult.Slot));
            broadcastPackets.Add(V113PetPackets.ShowPetLevelUp(player.Character.Id, petResult.Slot));
        }

        selfPackets.Add(V113PetPackets.UpdatePet(petResult.Pet, petResult.CashSlot));
        selfPackets.AddRange(result.Packets);
        broadcastPackets.Add(V113PetPackets.PetCommand(
            player.Character.Id,
            petResult.Slot,
            command: 1,
            success: true,
            food: true));

        return result with
        {
            Packets = selfPackets,
            BroadcastPackets = broadcastPackets,
        };
    }

    private static V113UseCashItemResult HandleChalkboard(PacketReader reader, Player player)
    {
        string message;
        try
        {
            message = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return EnableActionsOnly();
        }

        if (player.Character.MapId / 1_000_000 == 109)
        {
            return EnableActionsOnly();
        }

        player.SetChalkboard(message);
        return new V113UseCashItemResult(true, false, One(V113StatsPackets.EnableActions()))
        {
            MapPackets = One(V113ChalkboardPackets.Chalkboard(player.Character.Id, player.ChalkboardMessage)),
        };
    }

    private V113UseCashItemResult ConsumeCashItem(Player player, short slot, int itemId)
    {
        bool consumed = player.TryTakeItemFromSlot(InventoryType.Cash, slot, itemId, 1, out var mutation);
        if (!consumed || mutation is null)
        {
            return EnableActionsOnly();
        }

        player.FlushInventory();
        return new V113UseCashItemResult(
            true,
            true,
            new[]
            {
                V113ShopPackets.ModifyInventoryQuantity(mutation),
                V113StatsPackets.EnableActions(),
            });
    }

    private V113UseCashItemResult ConsumeCashItemWithPackets(
        Player player,
        short slot,
        int itemId,
        IReadOnlyList<byte[]> packetsBeforeConsume)
    {
        bool consumed = player.TryTakeItemFromSlot(InventoryType.Cash, slot, itemId, 1, out var mutation);
        if (!consumed || mutation is null)
        {
            return EnableActionsOnly();
        }

        player.FlushInventory();

        var packets = new List<byte[]>(packetsBeforeConsume.Count + 2);
        packets.AddRange(packetsBeforeConsume);
        packets.Add(V113ShopPackets.ModifyInventoryQuantity(mutation));
        packets.Add(V113StatsPackets.EnableActions());

        return new V113UseCashItemResult(true, true, packets);
    }

    private V113UseCashItemResult HandleStub(int itemId, string reason)
    {
        _log.LogDebug("[UseCashItem] Deferred cash item {ItemId}: {Reason}", itemId, reason);
        return EnableActionsOnly();
    }

    private V113UseCashItemResult HandleUnknown(int itemId)
    {
        _log.LogDebug("[UseCashItem] Unhandled cash item {ItemId}", itemId);
        return EnableActionsOnly();
    }

    private static V113UseCashItemResult EnableActionsOnly()
        => new(true, false, One(V113StatsPackets.EnableActions()));

    private static IReadOnlyList<byte[]> One(byte[] packet) => new[] { packet };

    private static bool TryReadInventoryTarget(PacketReader reader, out InventoryType type, out short slot)
    {
        type = default;
        slot = 0;
        if (reader.Remaining < 8)
        {
            return false;
        }

        var rawType = (byte)reader.ReadInt();
        if (!InventoryTypes.IsValid(rawType))
        {
            _ = reader.ReadInt();
            return false;
        }

        type = (InventoryType)rawType;
        slot = ReadSignedByteFromInt(reader);
        return true;
    }

    private static short ReadSignedByteFromInt(PacketReader reader)
        => (short)(sbyte)(byte)reader.ReadInt();

    private static AbilityPointTarget? MapBasicAbilityPointTarget(int rawStat)
        => rawStat switch
        {
            0x40 => AbilityPointTarget.Str,
            0x80 => AbilityPointTarget.Dex,
            0x100 => AbilityPointTarget.Int,
            0x200 => AbilityPointTarget.Luk,
            _ => null,
        };

    private static bool CanHammer(Equip equip)
        => equip.ItemId is not 1122000 and not 1122076 &&
           equip.ItemId / 100000 is not 19 and not 16;

    private static long ReadLong(PacketReader reader)
    {
        var low = (uint)reader.ReadInt();
        var high = (uint)reader.ReadInt();
        return unchecked((long)(((ulong)high << 32) | low));
    }

    private static int GetPetFlagValue(int itemId) => itemId switch
    {
        5190000 or 5191000 => PetConstants.ItemPickupFlag,
        5190001 or 5191001 => PetConstants.HpChargeFlag,
        5190002 or 5191002 => PetConstants.ExpandPickupFlag,
        5190003 or 5191003 => PetConstants.AutoPickupFlag,
        5190004 or 5191004 => PetConstants.LeftoverPickupFlag,
        5190005 => PetConstants.UnpickableFlag,
        5190006 => PetConstants.MpChargeFlag,
        5190007 => PetConstants.PetDrawFlag,
        5190008 => PetConstants.PetDialogueFlag,
        _ => 0,
    };
}
