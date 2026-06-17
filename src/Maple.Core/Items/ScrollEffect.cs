namespace Maple.Core.Items;

public sealed record ScrollEffect(
    int ScrollId,
    int SuccessRate,
    bool Cursed,
    short Str,
    short Dex,
    short Int,
    short Luk,
    short Hp,
    short Mp,
    short Watk,
    short Matk,
    short Wdef,
    short Mdef,
    short Acc,
    short Avoid,
    short Speed,
    short Jump);

public interface IScrollCatalog
{
    ScrollEffect? GetScroll(int scrollId);
}
