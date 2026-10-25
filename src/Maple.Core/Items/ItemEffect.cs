namespace Maple.Core.Items;

/// <param name="Cures">P127：解除的異常（Java spec poison/seal/darkness/weakness/curse → cureDebuffs）。</param>
public sealed record ItemEffect(int ItemId, int Hp, int Mp, int HpRate, int MpRate, IReadOnlyList<World.PlayerDisease>? Cures = null);

public interface IItemEffectCatalog
{
    ItemEffect? GetEffect(int itemId);
}
