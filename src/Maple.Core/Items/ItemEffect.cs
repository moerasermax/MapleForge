namespace Maple.Core.Items;

public sealed record ItemEffect(int ItemId, int Hp, int Mp, int HpRate, int MpRate);

public interface IItemEffectCatalog
{
    ItemEffect? GetEffect(int itemId);
}
