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
            5090000 or 5090100 => await HandleNoteAsync(reader, player, slot, itemId, ct).ConfigureAwait(false),
            5100000 => HandleCongratulatorySong(player, slot, itemId),
            5170000 => HandlePetName(reader, player, slot, itemId),
            >= 5190000 and <= 5190008 => HandlePetSkill(reader, player, slot, itemId),
            >= 5191000 and <= 5191004 => HandlePetSkill(reader, player, slot, itemId),
            >= 5240000 and <= 5240028 => HandleCashPetFood(player, slot, itemId),
            5370000 or 5370001 => HandleChalkboard(reader, player),
            _ => HandleUnknown(itemId),
        };
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

    private V113UseCashItemResult HandleUnknown(int itemId)
    {
        _log.LogDebug("[UseCashItem] Unhandled cash item {ItemId}", itemId);
        return EnableActionsOnly();
    }

    private static V113UseCashItemResult EnableActionsOnly()
        => new(true, false, One(V113StatsPackets.EnableActions()));

    private static IReadOnlyList<byte[]> One(byte[] packet) => new[] { packet };

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
