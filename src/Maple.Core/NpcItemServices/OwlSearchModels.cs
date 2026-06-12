using Maple.Core.Inventory;

namespace Maple.Core.NpcItemServices;

public sealed record OwlSearchEntry(
    string OwnerName,
    int MapId,
    string Description,
    int Quantity,
    int Bundles,
    int Price,
    int ListingObjectId,
    byte ChannelIndex,
    InventoryType InventoryType,
    ItemRecord? EquipItem = null);

public interface IOwlSearchCatalog
{
    IReadOnlyList<OwlSearchEntry> Search(int itemId);
}
