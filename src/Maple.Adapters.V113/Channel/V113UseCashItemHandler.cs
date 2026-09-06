using Maple.Application.Maps;
using Maple.Application.Pets;
using Maple.Application.NpcItemServices;
using Maple.Application.Social;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Maps;
using Maple.Core.Pets;
using Maple.Core.Shops;
using Maple.Core.World;
using Microsoft.Extensions.Logging;

namespace Maple.Adapters.V113.Channel;

/// <summary>Result of handling USE_CASH_ITEM (0x49).</summary>
internal sealed record V113UseCashItemResult(
    bool Handled,
    bool CharacterMutated,
    IReadOnlyList<byte[]> Packets,
    int? WarpToMapId = null)
{
    public IReadOnlyList<byte[]> BroadcastPackets { get; init; } = Array.Empty<byte[]>();

    public IReadOnlyList<byte[]> MapPackets { get; init; } = Array.Empty<byte[]>();

    /// <summary>
    /// 對照 Java <c>World.Broadcast.broadcastSmega</c>（全服，經 <c>ChannelServer.broadcastSmega</c>
    /// 對 <c>PlayerStorage</c> 全體廣播，含發送者本人）：送給目前所有在線玩家，不限地圖。
    /// MapleForge 現行單實例單頻道架構下，這與 Java「頻道範圍」廣播（如 5071000）實質等價。
    /// </summary>
    public IReadOnlyList<byte[]> ChannelBroadcastPackets { get; init; } = Array.Empty<byte[]>();
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
    private readonly MapService _mapService;
    private readonly ILogger<V113UseCashItemHandler> _log;

    public V113UseCashItemHandler(
        OwlService owlService,
        PetService petService,
        NoteService noteService,
        MapService mapService,
        ILogger<V113UseCashItemHandler> log)
    {
        _owlService = owlService;
        _petService = petService;
        _noteService = noteService;
        _mapService = mapService;
        _log = log;
    }

    /// <summary>
    /// 對照 Java <c>InventoryHandler.UseTeleRock</c>／<c>UseCashItem</c> 任意門分支共用的
    /// VipRock 場地限制守門：目前地圖或目標地圖任一設了 <see cref="FieldLimitType.VipRock"/>
    /// 旗標就整段擋下（靜默略過，不送錯誤訊息，對照 Java 該旗標成立時直接跳過 changeMap）。
    /// </summary>
    private bool IsVipRockWarpBlocked(Player player, int targetMapId)
        => FieldLimitType.VipRock.Check(_mapService.LoadMap(player.Character.MapId).FieldLimit) ||
           FieldLimitType.VipRock.Check(_mapService.LoadMap(targetMapId).FieldLimit);

    /// <summary>對照 Java <c>MapConstants.isMapleLand</c>：楓之島（新手教學島）地圖 ID 一律小於此值。</summary>
    private static bool IsMapleLand(int mapId) => mapId < 1010004;

    internal V113UseCashItemResult Handle(PacketReader reader, Player player, int channel = 1)
        => HandleAsync(reader, player, channel, CancellationToken.None).GetAwaiter().GetResult();

    internal Task<V113UseCashItemResult> HandleAsync(
        PacketReader reader,
        Player player,
        CancellationToken ct = default)
        => HandleAsync(reader, player, channel: 1, ct);

    internal async Task<V113UseCashItemResult> HandleAsync(
        PacketReader reader,
        Player player,
        int channel,
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
            2320000 or 5040000 or 5040001 or 5041000 => HandleCashTeleportRock(reader, player, slot, itemId),
            5042000 => HandleFixedDestinationTeleport(player, slot, itemId, mapId: 701000200),
            5042001 => HandleFixedDestinationTeleport(player, slot, itemId, mapId: 741000000),
            5050000 => HandleApReset(reader, player, slot, itemId),
            >= 5050001 and <= 5050004 => HandleSpReset(reader, player, slot, itemId),
            5060000 => HandleItemTag(reader, player, slot, itemId),
            5060001 => HandleSealingLock(reader, player, slot, itemId, lockDays: 0),
            5060003 => HandleStub(itemId, "Peanut incubator reward"),
            5060005 => HandleStub(itemId, "Incubator unavailable"),
            5061000 => HandleSealingLock(reader, player, slot, itemId, lockDays: 7),
            5061001 => HandleSealingLock(reader, player, slot, itemId, lockDays: 30),
            5061002 => HandleSealingLock(reader, player, slot, itemId, lockDays: 90),
            5070000 => HandleMegaphone(
                reader,
                player,
                slot,
                itemId,
                channel,
                static (message, _, _) => V113BroadcastPackets.Megaphone(message)),
            // 對照 Java：5071000 讀 message+ear 但建包只呼叫 getMegaphone(message)（ear 是 Java 原始碼
            // 本身的死變數，忠實重現、不修正），封包格式是 plain Megaphone，非 SuperMegaphone；範圍是
            // c.getChannelServer().broadcastSmega＝頻道範圍。
            5071000 => HandleMegaphone(
                reader,
                player,
                slot,
                itemId,
                channel,
                static (message, _, _) => V113BroadcastPackets.Megaphone(message),
                broadcastChannelWide: true),
            // 5072000 是 World.Broadcast.broadcastSmega（全服）範圍 + getSuperMegaphone 格式。
            5072000 => HandleMegaphone(
                reader,
                player,
                slot,
                itemId,
                channel,
                static (message, broadcastChannel, ear) => V113BroadcastPackets.SuperMegaphone(message, broadcastChannel, ear),
                broadcastChannelWide: true),
            5073000 => HandleMegaphone(
                reader,
                player,
                slot,
                itemId,
                channel,
                static (message, broadcastChannel, ear) => V113BroadcastPackets.HeartMegaphone(message, broadcastChannel, ear),
                broadcastChannelWide: true),
            5074000 => HandleMegaphone(
                reader,
                player,
                slot,
                itemId,
                channel,
                static (message, broadcastChannel, ear) => V113BroadcastPackets.SkullMegaphone(message, broadcastChannel, ear),
                broadcastChannelWide: true),
            >= 5075000 and <= 5075002 => HandleMapleTvStub(itemId),
            5076000 => HandleItemMegaphone(reader, player, slot, itemId, channel),
            5077000 => HandleTripleMegaphone(reader, player, slot, itemId, channel),
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
            >= 5390000 and <= 5390006 => HandleAvatarMegaphoneFallback(reader, player, slot, itemId, channel),
            5390029 => HandleAvatarMegaphoneFallback(reader, player, slot, itemId, channel),
            5450000 => HandleStub(itemId, "Travelling merchant"),
            5520000 or 5520001 => HandleKarma(reader, player, slot, itemId),
            5560000 or 5561000 => HandleAnyDoorTicket(reader, player, slot, itemId),
            5570000 => HandleViciousHammer(reader, player, slot, itemId),
            5610000 or 5610001 => HandleVegaStub(reader, itemId),
            _ => HandleUnknown(itemId),
        };
    }

    private V113UseCashItemResult HandleMegaphone(
        PacketReader reader,
        Player player,
        short slot,
        int itemId,
        int channel,
        Func<string, int, bool, byte[]> buildPacket,
        int maxMessageLength = 65,
        bool broadcastChannelWide = false)
    {
        if (!TryReadMegaphoneMessage(reader, player, maxMessageLength, out var message, out var ear))
        {
            return EnableActionsOnly();
        }

        var packet = buildPacket(message, NormalizeChannel(channel), ear);
        return broadcastChannelWide
            ? ConsumeCashItemWithChannelBroadcast(player, slot, itemId, packet)
            : ConsumeCashItemWithMapPacket(player, slot, itemId, packet);
    }

    private V113UseCashItemResult HandleTripleMegaphone(
        PacketReader reader,
        Player player,
        short slot,
        int itemId,
        int channel)
    {
        if (!CanUseMegaphone(player) || reader.Remaining < 1)
        {
            return EnableActionsOnly();
        }

        var lineCount = reader.ReadByte();
        if (lineCount is < 1 or > 3)
        {
            return EnableActionsOnly();
        }

        var messages = new string[lineCount];
        for (var i = 0; i < lineCount; i++)
        {
            string line;
            try
            {
                line = reader.ReadMapleString();
            }
            catch (InvalidDataException)
            {
                return EnableActionsOnly();
            }

            if (line.Length > 65)
            {
                return EnableActionsOnly();
            }

            messages[i] = FormatMegaphoneMessage(player, line);
        }

        if (reader.Remaining < 1)
        {
            return EnableActionsOnly();
        }

        var ear = reader.ReadByte() > 0;
        var packet = V113BroadcastPackets.TripleMegaphone(messages, NormalizeChannel(channel), ear);
        return ConsumeCashItemWithChannelBroadcast(player, slot, itemId, packet);
    }

    private V113UseCashItemResult HandleItemMegaphone(
        PacketReader reader,
        Player player,
        short slot,
        int itemId,
        int channel)
    {
        if (!TryReadMegaphoneMessage(reader, player, maxMessageLength: 65, out var message, out var ear) ||
            reader.Remaining < 1)
        {
            return EnableActionsOnly();
        }

        Item? item = null;
        var hasItem = reader.ReadByte() == 1;
        if (hasItem)
        {
            if (!TryReadInventoryTarget(reader, out var type, out var itemSlot))
            {
                return EnableActionsOnly();
            }

            item = player.Inventory.By(type).Get(itemSlot);
        }

        var packet = V113BroadcastPackets.ItemMegaphone(message, NormalizeChannel(channel), ear, item);
        return ConsumeCashItemWithChannelBroadcast(player, slot, itemId, packet);
    }

    private V113UseCashItemResult HandleAvatarMegaphoneFallback(
        PacketReader reader,
        Player player,
        short slot,
        int itemId,
        int channel)
        => HandleMegaphone(
            reader,
            player,
            slot,
            itemId,
            channel,
            static (message, broadcastChannel, ear) => V113BroadcastPackets.SuperMegaphone(message, broadcastChannel, ear),
            maxMessageLength: 55,
            broadcastChannelWide: true);

    private V113UseCashItemResult HandleMapleTvStub(int itemId)
    {
        _log.LogDebug("[UseCashItem] MapleTV cash item {ItemId} deferred: no MapleTV broadcast system", itemId);
        return EnableActionsOnly();
    }

    private V113UseCashItemResult HandleFixedDestinationTeleport(
        Player player,
        short slot,
        int itemId,
        int mapId)
        => ConsumeCashItemForWarp(player, slot, itemId, mapId);

    private V113UseCashItemResult HandleCashTeleportRock(PacketReader reader, Player player, short slot, int itemId)
    {
        if (reader.Remaining < 1)
        {
            return EnableActionsOnly();
        }

        var mode = reader.ReadByte();
        if (mode == 0)
        {
            if (reader.Remaining < 4)
            {
                return EnableActionsOnly();
            }

            var mapId = reader.ReadInt();
            // 對照 Java UseTeleRock：5041000（高級順移之石）要求目標地圖在玩家的高級傳送石
            // 已註冊清單裡，其餘（2320000/5040000/5040001）要求在一般傳送石已註冊清單裡。
            var isRegisteredRockMap = itemId == 5041000 ? player.IsVipRockMap(mapId) : player.IsRegularRockMap(mapId);
            // TODO: Apply continent check once MapleForge has full rock-map persistence semantics
            // for this flow（見 P041：VipRock；P042：MapleLand；P043：已註冊地圖清單已接線）。
            if (IsMapleLand(player.Character.MapId) || IsMapleLand(mapId) || !isRegisteredRockMap ||
                IsVipRockWarpBlocked(player, mapId))
            {
                return EnableActionsOnly();
            }

            return ConsumeCashItemForWarp(player, slot, itemId, mapId);
        }

        if (mode == 1)
        {
            try
            {
                var targetName = reader.ReadMapleString();
                _log.LogDebug(
                    "[UseCashItem] Teleport rock player mode deferred itemId={ItemId} target={Target}",
                    itemId,
                    targetName);
            }
            catch (InvalidDataException)
            {
                _log.LogDebug("[UseCashItem] Teleport rock player mode malformed itemId={ItemId}", itemId);
            }
        }

        return EnableActionsOnly();
    }

    private V113UseCashItemResult HandleAnyDoorTicket(PacketReader reader, Player player, short slot, int itemId)
    {
        if (reader.Remaining < 1)
        {
            return EnableActionsOnly();
        }

        var mode = reader.ReadByte();
        if (mode != 0 || reader.Remaining < 4)
        {
            _log.LogDebug("[UseCashItem] Any-door deferred itemId={ItemId} mode={Mode}", itemId, mode);
            return EnableActionsOnly();
        }

        var mapId = reader.ReadInt();
        if (mapId <= 2_000_000)
        {
            return EnableActionsOnly();
        }

        // TODO: Apply event-instance check once that subsystem exists in MapleForge
        // （見 P041：VipRock；P042：MapleLand 已接線）。
        if (IsMapleLand(player.Character.MapId) || IsVipRockWarpBlocked(player, mapId))
        {
            return EnableActionsOnly();
        }

        return ConsumeCashItemForWarp(player, slot, itemId, mapId);
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

    private V113UseCashItemResult ConsumeCashItemForWarp(Player player, short slot, int itemId, int mapId)
    {
        var result = ConsumeCashItem(player, slot, itemId);
        return result.CharacterMutated
            ? result with { WarpToMapId = mapId }
            : result;
    }

    private V113UseCashItemResult ConsumeCashItemWithMapPacket(
        Player player,
        short slot,
        int itemId,
        byte[] mapPacket)
    {
        var result = ConsumeCashItem(player, slot, itemId);
        return result.CharacterMutated
            ? result with { MapPackets = One(mapPacket) }
            : result;
    }

    /// <summary>對照 Java <c>World.Broadcast.broadcastSmega</c>／<c>ChannelServer.broadcastSmega</c>（見型別註解）。</summary>
    private V113UseCashItemResult ConsumeCashItemWithChannelBroadcast(
        Player player,
        short slot,
        int itemId,
        byte[] channelPacket)
    {
        var result = ConsumeCashItem(player, slot, itemId);
        return result.CharacterMutated
            ? result with { ChannelBroadcastPackets = One(channelPacket) }
            : result;
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

    private static bool TryReadMegaphoneMessage(
        PacketReader reader,
        Player player,
        int maxMessageLength,
        out string formattedMessage,
        out bool ear)
    {
        formattedMessage = string.Empty;
        ear = false;
        if (!CanUseMegaphone(player))
        {
            return false;
        }

        string message;
        try
        {
            message = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return false;
        }

        if (message.Length > maxMessageLength || reader.Remaining < 1)
        {
            return false;
        }

        ear = reader.ReadByte() != 0;
        formattedMessage = FormatMegaphoneMessage(player, message);
        return true;
    }

    private static bool CanUseMegaphone(Player player)
        => player.Character.Level >= 10;

    private static string FormatMegaphoneMessage(Player player, string message)
        => $"{player.Character.Name} : {message}";

    private static int NormalizeChannel(int channel)
        => channel <= 0 ? 1 : channel;

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
