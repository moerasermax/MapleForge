using Maple.Application.Items;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113UseUpgradeScrollRequest(
    int Tick,
    short ScrollSlot,
    short EquipSlot,
    short Flags)
{
    public bool WhiteScroll => (Flags & 2) == 2;
}

internal sealed record V113ScrollHandleResult(
    bool Handled,
    bool CharacterMutated,
    ScrollUseResult Use,
    IReadOnlyList<byte[]> SelfPackets,
    byte[]? BroadcastPacket);

internal static class V113ScrollPackets
{
    public const short RecvUseUpgradeScroll = 0x50;
    public const short SendShowScrollEffect = unchecked((short)0x9F);

    public static V113UseUpgradeScrollRequest ParseUseUpgradeScroll(PacketReader reader)
    {
        var tick = reader.ReadInt();
        var scrollSlot = reader.ReadShort();
        var equipSlot = reader.ReadShort();
        var flags = reader.ReadShort();
        return new V113UseUpgradeScrollRequest(tick, scrollSlot, equipSlot, flags);
    }

    public static byte[] ShowScrollEffect(
        int characterId,
        ScrollResult result,
        bool legendarySpirit,
        bool whiteScroll)
    {
        var w = new PacketWriter(11);
        w.WriteShort(V113ChannelSendOp.ShowScrollEffect);
        w.WriteInt(characterId);
        w.WriteByte(result == ScrollResult.Success ? (byte)1 : (byte)0);
        w.WriteByte(result == ScrollResult.Curse ? (byte)1 : (byte)0);
        w.WriteShort(legendarySpirit ? (short)1 : (short)0);
        w.WriteByte(whiteScroll ? (byte)1 : (byte)0);
        return w.ToArray();
    }

    public static byte[] ModifyInventoryEquipUpdate(short slot, Equip equip)
    {
        var w = BeginInventoryModify(updateTick: true, modCount: 2);
        WriteRemove(w, InventoryType.Equip, slot);
        WriteAdd(w, InventoryType.Equip, slot, equip);
        return w.ToArray();
    }

    public static byte[] ModifyInventoryEquipRemove(short slot)
    {
        var w = BeginInventoryModify(updateTick: true, modCount: 1);
        WriteRemove(w, InventoryType.Equip, slot);
        return w.ToArray();
    }

    public static byte[] ModifyInventoryQuantity(InventoryQuantityMutation mutation)
        => V113ItemUsePackets.ModifyInventoryQuantity(mutation);

    private static PacketWriter BeginInventoryModify(bool updateTick, byte modCount)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.ModifyInventoryItem);
        w.WriteByte(updateTick ? (byte)1 : (byte)0);
        w.WriteByte(modCount);
        return w;
    }

    private static void WriteRemove(PacketWriter w, InventoryType type, short slot)
    {
        w.WriteByte(3);
        w.WriteByte((byte)type);
        w.WriteShort(slot);
    }

    private static void WriteAdd(PacketWriter w, InventoryType type, short slot, Item item)
    {
        w.WriteByte(0);
        w.WriteByte((byte)type);
        w.WriteShort(slot);
        AddItemInfo(w, item);
    }

    private static void AddItemInfo(PacketWriter w, Item item)
    {
        w.WriteByte(item.IsEquip ? 1 : 2);
        w.WriteInt(item.ItemId);
        w.WriteByte(0);
        w.WriteLong(GetTime(item.Expiration));

        if (item is Equip equip)
        {
            w.WriteByte(equip.UpgradeSlots);
            w.WriteByte(equip.Level);
            w.WriteShort(equip.Str);
            w.WriteShort(equip.Dex);
            w.WriteShort(equip.Int);
            w.WriteShort(equip.Luk);
            w.WriteShort(equip.Hp);
            w.WriteShort(equip.Mp);
            w.WriteShort(equip.Watk);
            w.WriteShort(equip.Matk);
            w.WriteShort(equip.Wdef);
            w.WriteShort(equip.Mdef);
            w.WriteShort(equip.Acc);
            w.WriteShort(equip.Avoid);
            w.WriteShort(equip.Hands);
            w.WriteShort(equip.Speed);
            w.WriteShort(equip.Jump);
            w.WriteMapleString(equip.Owner);
            w.WriteShort(equip.Flag);
            w.WriteByte(0);
            w.WriteByte(equip.ItemLevel);
            w.WriteInt(equip.ItemExp);
            w.WriteLong(equip.UniqueId);
            w.WriteLong(GetTime(-2));
            w.WriteInt(-1);
            return;
        }

        w.WriteShort(item.Quantity);
        w.WriteMapleString(item.Owner);
        w.WriteShort(item.Flag);
    }

    private static long GetTime(long offset)
    {
        const long KoreanEpochOffset = 116444736000000000L;
        if (offset < 0)
        {
            return KoreanEpochOffset + offset;
        }

        return KoreanEpochOffset + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 10000);
    }
}

public sealed class V113ScrollHandler
{
    private readonly ScrollService _scrolls;

    public V113ScrollHandler(ScrollService scrolls)
    {
        _scrolls = scrolls;
    }

    internal V113ScrollHandleResult HandleUseUpgradeScroll(PacketReader reader, Player player)
    {
        var request = V113ScrollPackets.ParseUseUpgradeScroll(reader);
        var result = _scrolls.UseScroll(
            player,
            request.ScrollSlot,
            request.EquipSlot,
            request.WhiteScroll,
            request.Tick);

        if (!result.Applied)
        {
            return new V113ScrollHandleResult(
                Handled: true,
                CharacterMutated: false,
                result,
                SelfPackets: new[] { V113StatsPackets.EnableActions() },
                BroadcastPacket: null);
        }

        var packets = new List<byte[]>(result.InventoryMutations.Count + 1);
        foreach (var mutation in result.InventoryMutations)
        {
            packets.Add(V113ScrollPackets.ModifyInventoryQuantity(mutation));
        }

        if (result.EquipDestroyed)
        {
            packets.Add(V113ScrollPackets.ModifyInventoryEquipRemove(result.EquipSlot));
        }
        else if (result.UpdatedEquip is not null)
        {
            packets.Add(V113ScrollPackets.ModifyInventoryEquipUpdate(result.EquipSlot, result.UpdatedEquip));
        }

        return new V113ScrollHandleResult(
            Handled: true,
            CharacterMutated: true,
            result,
            packets,
            V113ScrollPackets.ShowScrollEffect(
                player.Character.Id,
                result.Result,
                legendarySpirit: false,
                whiteScroll: result.WhiteScrollUsed));
    }
}
