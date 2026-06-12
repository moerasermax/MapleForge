using Maple.Application.NpcItemServices;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.NpcItemServices;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113OwlMinervaRequest(short Slot, int ItemId, int SearchItemId);

internal readonly record struct V113OwlWarpRequest(int ListingObjectId, int MapId);

internal static class V113OwlPackets
{
    public const short RecvOwl = 0x3C;
    public const short RecvOwlWarp = 0x3D;
    public const short RecvUseOwlMinerva = 0x4D;
    public const short SendShopScannerResult = 0x3F;
    public const short SendShopLinkResult = 0x40;

    private static readonly int[] DefaultOwlItems =
    [
        1082002, 2070005, 2070006, 1022047, 1102041,
        2044705, 2340000, 2040017, 1092030, 2040804,
    ];

    public static V113OwlMinervaRequest ParseMinerva(PacketReader reader)
    {
        if (reader.Remaining < 10)
        {
            throw new InvalidDataException("USE_OWL_MINERVA requires short slot, int itemId, int searchItemId.");
        }

        return new V113OwlMinervaRequest(reader.ReadShort(), reader.ReadInt(), reader.ReadInt());
    }

    public static int ParseSearchItem(PacketReader reader)
    {
        if (reader.Remaining < 4)
        {
            throw new InvalidDataException("OWL search requires int itemId.");
        }

        return reader.ReadInt();
    }

    public static V113OwlWarpRequest ParseWarp(PacketReader reader)
    {
        if (reader.Remaining < 8)
        {
            throw new InvalidDataException("OWL_WARP requires int listing id and int map id.");
        }

        return new V113OwlWarpRequest(reader.ReadInt(), reader.ReadInt());
    }

    public static byte[] OwlOpen()
    {
        var w = new PacketWriter(2 + 1 + 1 + (DefaultOwlItems.Length * 4));
        w.WriteShort(SendShopScannerResult);
        w.WriteByte(7);
        w.WriteByte((byte)DefaultOwlItems.Length);
        foreach (var itemId in DefaultOwlItems)
        {
            w.WriteInt(itemId);
        }

        return w.ToArray();
    }

    public static byte[] OwlSearched(int itemSearch, IReadOnlyList<OwlSearchEntry> entries)
    {
        var w = new PacketWriter();
        w.WriteShort(SendShopScannerResult);
        w.WriteByte(6);
        w.WriteInt(0);
        w.WriteInt(itemSearch);
        w.WriteInt(entries.Count);

        foreach (var entry in entries)
        {
            w.WriteMapleString(entry.OwnerName);
            w.WriteInt(entry.MapId);
            w.WriteMapleString(entry.Description);
            w.WriteInt(entry.Quantity);
            w.WriteInt(entry.Bundles);
            w.WriteInt(entry.Price);
            w.WriteInt(entry.ListingObjectId);
            w.WriteByte(entry.ChannelIndex);
            w.WriteByte((byte)entry.InventoryType);
            if (entry.InventoryType == InventoryType.Equip)
            {
                AddItemInfo(
                    w,
                    entry.EquipItem ?? new ItemRecord
                    {
                        Type = (byte)InventoryType.Equip,
                        IsEquip = true,
                        ItemId = itemSearch,
                        Quantity = 1,
                        Expiration = -1,
                    });
            }
        }

        return w.ToArray();
    }

    private static void AddItemInfo(PacketWriter w, ItemRecord record)
    {
        if (record.IsEquip)
        {
            w.WriteByte(1);
            w.WriteInt(record.ItemId);
            w.WriteByte(0);
            w.WriteLong(GetTime(record.Expiration));
            w.WriteByte(record.UpgradeSlots);
            w.WriteByte(record.Level);
            w.WriteShort(record.Str); w.WriteShort(record.Dex); w.WriteShort(record.Int); w.WriteShort(record.Luk);
            w.WriteShort(record.Hp); w.WriteShort(record.Mp); w.WriteShort(record.Watk); w.WriteShort(record.Matk);
            w.WriteShort(record.Wdef); w.WriteShort(record.Mdef); w.WriteShort(record.Acc); w.WriteShort(record.Avoid);
            w.WriteShort(record.Hands); w.WriteShort(record.Speed); w.WriteShort(record.Jump);
            w.WriteMapleString(record.Owner);
            w.WriteShort(record.Flag);
            w.WriteByte(0);
            w.WriteByte(record.ItemLevel);
            w.WriteInt(record.ItemExp);
            w.WriteLong(record.UniqueId);
            w.WriteLong(GetTime(-2));
            w.WriteInt(-1);
            return;
        }

        w.WriteByte(2);
        w.WriteInt(record.ItemId);
        w.WriteByte(0);
        w.WriteLong(GetTime(record.Expiration));
        w.WriteShort(record.Quantity);
        w.WriteMapleString(record.Owner);
        w.WriteShort(record.Flag);
    }

    private static long GetTime(long offset)
    {
        const long KoreanEpochOffset = 116444736000000000L;
        if (offset < 0)
        {
            return KoreanEpochOffset + offset;
        }

        return KoreanEpochOffset + (offset * 10000);
    }
}

internal sealed record V113OwlHandleResult(
    bool Handled,
    bool CharacterMutated,
    int? WarpMapId,
    IReadOnlyList<byte[]> Packets);

public sealed class V113OwlHandler
{
    private readonly OwlService _owls;

    public V113OwlHandler(OwlService owls)
    {
        _owls = owls;
    }

    internal V113OwlHandleResult HandleOpen(Player player)
    {
        if (_owls.CanOpenOwl(player))
        {
            return Packets([V113OwlPackets.OwlOpen()]);
        }

        return Packets([V113StatsPackets.EnableActions()]);
    }

    internal V113OwlHandleResult HandleSearch(PacketReader reader, Player player)
    {
        OwlSearchResult result;
        try
        {
            result = _owls.Search(player, V113OwlPackets.ParseSearchItem(reader));
        }
        catch (InvalidDataException)
        {
            return Packets([V113StatsPackets.EnableActions()]);
        }

        return EncodeSearch(result);
    }

    internal V113OwlHandleResult HandleMinerva(PacketReader reader, Player player)
    {
        OwlSearchResult result;
        try
        {
            var request = V113OwlPackets.ParseMinerva(reader);
            result = _owls.UseMinerva(player, request.Slot, request.ItemId, request.SearchItemId);
        }
        catch (InvalidDataException)
        {
            return Packets([V113StatsPackets.EnableActions()]);
        }

        return EncodeSearch(result, alwaysEnableActions: true);
    }

    internal V113OwlHandleResult HandleWarp(PacketReader reader, Player player)
    {
        V113OwlWarpRequest request;
        try
        {
            request = V113OwlPackets.ParseWarp(reader);
        }
        catch (InvalidDataException)
        {
            return Packets([V113StatsPackets.EnableActions()]);
        }

        var decision = _owls.DecideWarp(player, request.MapId);
        return new V113OwlHandleResult(
            true,
            false,
            decision.CanWarp ? decision.MapId : null,
            new[] { V113StatsPackets.EnableActions() });
    }

    private static V113OwlHandleResult EncodeSearch(OwlSearchResult result, bool alwaysEnableActions = false)
    {
        var packets = new List<byte[]>();
        var mutated = false;

        if (result.Success)
        {
            packets.Add(V113OwlPackets.OwlSearched(result.ItemId, result.Entries));
            if (result.ConsumedItem is not null)
            {
                packets.Add(V113ShopPackets.ModifyInventoryQuantity(result.ConsumedItem));
                mutated = true;
            }
        }

        if (!result.Success || alwaysEnableActions)
        {
            packets.Add(V113StatsPackets.EnableActions());
        }

        return new V113OwlHandleResult(result.Success, mutated, null, packets);
    }

    private static V113OwlHandleResult Packets(IReadOnlyList<byte[]> packets)
        => new(true, false, null, packets);
}
