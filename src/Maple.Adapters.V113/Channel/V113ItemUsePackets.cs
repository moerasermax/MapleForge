using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113UseItemRequest(int Tick, short Slot, int ItemId);

internal readonly record struct V113UseCatchItemRequest(int Tick, short Slot, int ItemId, int MobObjectId);

internal static class V113ItemUsePackets
{
    public const short RecvUseSummonBag = 0x45;
    public const short RecvUseMountFood = 0x47;
    public const short RecvUseCatchItem = 0x4B;
    public const short RecvUseReturnScroll = 0x4F;

    public const short SendModifyInventoryItem = 0x1B;
    public const short SendSetTamingMobInfo = 0x2D;
    public const short SendCatchMonster = unchecked((short)0xF5);

    public static V113UseItemRequest ParseUseInventoryItem(PacketReader reader)
    {
        var tick = reader.ReadInt();
        var slot = reader.ReadShort();
        var itemId = reader.ReadInt();
        return new V113UseItemRequest(tick, slot, itemId);
    }

    public static V113UseCatchItemRequest ParseUseCatchItem(PacketReader reader)
    {
        var request = ParseUseInventoryItem(reader);
        var mobObjectId = reader.ReadInt();
        return new V113UseCatchItemRequest(request.Tick, request.Slot, request.ItemId, mobObjectId);
    }

    public static byte[] UpdateMount(int characterId, PlayerMountState mount, bool levelUp)
    {
        var w = new PacketWriter(23);
        w.WriteShort(SendSetTamingMobInfo);
        w.WriteInt(characterId);
        w.WriteInt(mount.Level);
        w.WriteInt(mount.Exp);
        w.WriteInt(mount.Fatigue);
        w.WriteByte(levelUp ? (byte)1 : (byte)0);
        return w.ToArray();
    }

    public static byte[] CatchMonster(int monsterId, int itemId, byte success)
    {
        var w = new PacketWriter(11);
        w.WriteShort(SendCatchMonster);
        w.WriteInt(monsterId);
        w.WriteInt(itemId);
        w.WriteByte(success);
        return w.ToArray();
    }

    public static byte[] ModifyInventoryQuantity(InventoryQuantityMutation mutation)
    {
        var mode = mutation.Removed ? 3 : 1;
        var w = BeginSingleInventoryModify(mode, mutation.Type, mutation.Slot);
        if (!mutation.Removed)
        {
            w.WriteShort(mutation.NewQuantity);
        }

        return w.ToArray();
    }

    public static byte[] ModifyInventoryAdd(InventoryType type, Item item)
    {
        var w = BeginSingleInventoryModify(mode: 0, type, item.Slot);
        AddItemInfo(w, item);
        return w.ToArray();
    }

    private static PacketWriter BeginSingleInventoryModify(int mode, InventoryType type, short slot)
    {
        var w = new PacketWriter();
        w.WriteShort(SendModifyInventoryItem);
        w.WriteByte(0);
        w.WriteByte(1);
        w.WriteByte(mode);
        w.WriteByte((byte)type);
        w.WriteShort(slot);
        return w;
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
