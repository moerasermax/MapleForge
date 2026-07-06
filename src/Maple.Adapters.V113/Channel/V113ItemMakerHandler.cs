using Maple.Application.Items;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal sealed record V113ItemMakerHandleResult(
    bool CharacterMutated,
    ItemMakerResult Result,
    IReadOnlyList<byte[]> SelfPackets,
    IReadOnlyList<byte[]> BroadcastPackets);

internal static class V113ItemMakerHandler
{
    public static ItemMakerRequest Parse(PacketReader reader)
    {
        var makerType = reader.ReadInt();
        return makerType switch
        {
            1 => ParseCreate(reader),
            3 => ItemMakerRequest.CreateCrystal(reader.ReadInt()),
            4 => ParseDisassemble(reader),
            _ => throw new InvalidDataException($"Unsupported ItemMaker makerType={makerType}."),
        };
    }

    public static V113ItemMakerHandleResult Handle(PacketReader reader, Player player, ItemMakerService service)
    {
        ItemMakerRequest request;
        try
        {
            request = Parse(reader);
        }
        catch (InvalidDataException)
        {
            return EnableOnly(ItemMakerResult.Failure(ItemMakerStatus.InvalidRequest, player.Character.Meso));
        }

        var result = service.Handle(player, request);
        if (!result.Success)
        {
            return EnableOnly(result);
        }

        var selfPackets = new List<byte[]>();
        foreach (var mutation in result.Mutations)
        {
            selfPackets.Add(V113ItemUsePackets.ModifyInventoryQuantity(mutation));
        }

        if (result.MesoChanged)
        {
            selfPackets.Add(V113ShopPackets.UpdateMeso(result.Meso));
        }

        if (result.CreatedItem is not null && result.CreatedInventoryType is { } type)
        {
            selfPackets.Add(V113ItemUsePackets.ModifyInventoryAdd(type, result.CreatedItem));
        }

        selfPackets.Add(ItemMakerSuccess());

        return new V113ItemMakerHandleResult(
            CharacterMutated: result.CharacterMutated,
            result,
            selfPackets,
            BroadcastPackets: [ItemMakerSuccessThirdParty(player.Character.Id)]);
    }

    private static ItemMakerRequest ParseCreate(PacketReader reader)
    {
        var toCreate = reader.ReadInt();
        if (reader.Remaining == 0)
        {
            return ItemMakerRequest.CreateItem(toCreate, useStimulator: false, Array.Empty<int>());
        }

        var useStimulator = reader.ReadByte() > 0;
        var numEnchanters = reader.ReadInt();
        if (numEnchanters is < 0 or > 128)
        {
            throw new InvalidDataException("Invalid ItemMaker enchanter count.");
        }

        var enchanters = new int[numEnchanters];
        for (var i = 0; i < enchanters.Length; i++)
        {
            enchanters[i] = reader.ReadInt();
        }

        return ItemMakerRequest.CreateItem(toCreate, useStimulator, enchanters);
    }

    private static ItemMakerRequest ParseDisassemble(PacketReader reader)
    {
        var itemId = reader.ReadInt();
        var tick = reader.ReadInt();
        var slot = (short)reader.ReadInt();
        return ItemMakerRequest.DisassembleEquip(itemId, tick, slot);
    }

    /// <summary>Java-source candidate/unverified: MaplePacketCreator.ItemMaker_Success().</summary>
    public static byte[] ItemMakerSuccess()
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.ShowItemGainInChat);
        w.WriteByte(0x11);
        w.WriteInt(0);
        return w.ToArray();
    }

    /// <summary>Java-source candidate/unverified: MaplePacketCreator.ItemMaker_Success_3rdParty(from).</summary>
    public static byte[] ItemMakerSuccessThirdParty(int characterId)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.ShowForeignEffect);
        w.WriteInt(characterId);
        w.WriteByte(0x11);
        w.WriteInt(0);
        return w.ToArray();
    }

    private static V113ItemMakerHandleResult EnableOnly(ItemMakerResult result)
        => new(
            CharacterMutated: false,
            result,
            SelfPackets: [V113StatsPackets.EnableActions()],
            BroadcastPackets: []);
}
