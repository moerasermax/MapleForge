using Maple.Application.Pets;
using Maple.Core.IO;
using Maple.Core.World;
using Maple.Net;

namespace Maple.Adapters.V113.Channel;

public static class V113PetHandler
{
    public static async Task HandleSpawnPetAsync(
        PacketReader reader,
        Player player,
        MapleSession session,
        PetService pets,
        Func<byte[], CancellationToken, Task> broadcast,
        CancellationToken ct)
    {
        V113SpawnPetRequest request;
        try
        {
            request = V113PetPackets.ParseSpawnPet(reader);
        }
        catch (InvalidDataException)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        var result = pets.SpawnPet(player, request.CashSlot, request.Lead);
        if (!result.Success || result.Pet is null)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        var packet = V113PetPackets.SpawnPet(player.Character.Id, result.Slot, result.Pet);
        await session.SendAsync(packet, ct);
        await broadcast(packet, ct);
    }

    public static async Task HandleMovePetAsync(
        PacketReader reader,
        Player player,
        MapleSession session,
        PetService pets,
        Func<byte[], CancellationToken, Task> broadcast,
        CancellationToken ct)
    {
        V113MovePetRequest request;
        try
        {
            request = V113PetPackets.ParseMovePet(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = pets.HandleMove(player, request.PetId, request.RawMovement);
        if (!result.Success || result.Pet is null)
        {
            return;
        }

        await broadcast(
            V113PetPackets.MovePet(player.Character.Id, result.Slot, result.Pet.PetId, result.RawMovement),
            ct);
    }

    public static async Task HandlePetFoodAsync(
        PacketReader reader,
        Player player,
        MapleSession session,
        PetService pets,
        Func<byte[], CancellationToken, Task> broadcast,
        CancellationToken ct)
    {
        V113PetFoodRequest request;
        try
        {
            request = V113PetPackets.ParsePetFood(reader);
        }
        catch (InvalidDataException)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        var gainCloseness = Random.Shared.Next(99) <= 50;
        var result = pets.FeedPet(player, request.Slot, request.ItemId, gainCloseness);
        if (!result.Success || result.Pet is null)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        if (result.Growth.LevelChanged && result.Pet.Level > 1)
        {
            await session.SendAsync(V113PetPackets.ShowOwnPetLevelUp(result.Slot), ct);
            await broadcast(V113PetPackets.ShowPetLevelUp(player.Character.Id, result.Slot), ct);
        }

        if (result.ConsumedItem is not null)
        {
            await session.SendAsync(V113ItemUsePackets.ModifyInventoryQuantity(result.ConsumedItem), ct);
        }

        await session.SendAsync(V113PetPackets.UpdatePet(result.Pet, result.CashSlot), ct);
        await broadcast(
            V113PetPackets.PetCommand(
                player.Character.Id,
                result.Slot,
                command: 1,
                success: result.Growth.FullnessIncreased,
                food: true),
            ct);
        await session.SendAsync(V113StatsPackets.EnableActions(), ct);
    }

    public static async Task HandlePetChatAsync(
        PacketReader reader,
        Player player,
        MapleSession session,
        PetService pets,
        Func<byte[], CancellationToken, Task> broadcast,
        CancellationToken ct)
    {
        V113PetChatRequest request;
        try
        {
            request = V113PetPackets.ParsePetChat(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = pets.HandleChat(player, request.PetId, request.Command, request.Text);
        if (!result.Success)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        await broadcast(
            V113PetPackets.PetChat(player.Character.Id, result.Slot, result.Command, result.Text),
            ct);
    }

    public static async Task HandlePetCommandAsync(
        PacketReader reader,
        Player player,
        MapleSession session,
        PetService pets,
        Func<byte[], CancellationToken, Task> broadcast,
        CancellationToken ct)
    {
        V113PetCommandRequest request;
        try
        {
            request = V113PetPackets.ParsePetCommand(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = pets.HandleCommand(player, request.PetId, request.Command);
        if (!result.Success || result.Pet is null)
        {
            return;
        }

        if (result.Growth.Success)
        {
            if (result.Growth.LevelChanged)
            {
                await session.SendAsync(V113PetPackets.ShowOwnPetLevelUp(result.Slot), ct);
                await broadcast(V113PetPackets.ShowPetLevelUp(player.Character.Id, result.Slot), ct);
            }

            await session.SendAsync(V113PetPackets.UpdatePet(result.Pet, result.CashSlot), ct);
        }

        await broadcast(
            V113PetPackets.PetCommand(
                player.Character.Id,
                result.Slot,
                result.Command,
                result.Growth.Success,
                food: false),
            ct);
    }

    public static async Task HandlePetAutoPotion(
        PacketReader reader,
        Player player,
        MapleSession session,
        PetService pets,
        CancellationToken ct)
    {
        V113PetAutoPotionRequest request;
        try
        {
            request = V113PetPackets.ParsePetAutoPot(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = pets.HandleAutoPotion(player, request.Slot);
        if (!result.Success)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
        }
    }

    public static async Task HandlePetIgnore(
        PacketReader reader,
        Player player,
        MapleSession session,
        PetService pets,
        CancellationToken ct)
    {
        V113PetIgnoreRequest request;
        try
        {
            request = V113PetPackets.ParsePetIgnore(reader);
        }
        catch (InvalidDataException)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        var result = pets.HandleIgnore(player, request.PetId, request.ExcludedItemIds);
        if (!result.Success || result.Pet is null)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        await session.SendAsync(
            V113PetPackets.LoadExceptionList(player.Character.Id, slot: 0, result.Pet),
            ct);
    }

    public static async Task HandlePetLootAsync(
        PacketReader reader,
        Player player,
        PetService pets,
        Func<byte[], CancellationToken, Task> broadcast,
        CancellationToken ct)
    {
        V113PetLootRequest request;
        try
        {
            request = V113PetPackets.ParsePetLoot(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var pet = pets.GetActivePet(player);
        if (pet is null || pet.PetId != request.PetId)
        {
            return;
        }

        await broadcast(
            V113DropPackets.RemoveItemFromMap(
                request.DropObjectId,
                animation: 5,
                characterId: player.Character.Id,
                petSlot: 0),
            ct);
    }
}
