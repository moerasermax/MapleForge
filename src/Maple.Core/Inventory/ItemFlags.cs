namespace Maple.Core.Inventory;

/// <summary>Item flag bits shared by inventory, trade, and item-service domain logic.</summary>
public static class ItemFlags
{
    public const short Lock = 0x01;

    public static bool Has(short flags, short flag) => (flags & flag) == flag;

    public static short Clear(short flags, short flag) => (short)(flags & ~flag);
}
