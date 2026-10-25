namespace Maple.Core.Items;

public sealed record ScrollEffect(
    int ScrollId,
    int SuccessRate,
    // P121：Java `cursed` 是百分比（失敗後 nextInt(99) < cursed 才毀損），不是布林。
    int CurseRate,
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
