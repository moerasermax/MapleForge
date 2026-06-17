using Maple.Core.Items;

namespace Maple.Application.Items;

public sealed class HardcodedScrollCatalog : IScrollCatalog
{
    public ScrollEffect? GetScroll(int scrollId) => scrollId switch
    {
        2040200 => Effect(scrollId, successRate: 100, str: 1),
        2040201 => Effect(scrollId, successRate: 60, str: 2),
        2040202 => Effect(scrollId, successRate: 10, cursed: true, str: 3),
        2044000 => Effect(scrollId, successRate: 100, watk: 1),
        2044001 => Effect(scrollId, successRate: 60, watk: 2),
        2044002 => Effect(scrollId, successRate: 10, cursed: true, watk: 3),
        >= 2040000 and <= 2049999 => Effect(scrollId, successRate: 100, watk: 1),
        _ => null,
    };

    private static ScrollEffect Effect(
        int scrollId,
        int successRate,
        bool cursed = false,
        short str = 0,
        short dex = 0,
        short @int = 0,
        short luk = 0,
        short hp = 0,
        short mp = 0,
        short watk = 0,
        short matk = 0,
        short wdef = 0,
        short mdef = 0,
        short acc = 0,
        short avoid = 0,
        short speed = 0,
        short jump = 0)
        => new(
            scrollId,
            successRate,
            cursed,
            str,
            dex,
            @int,
            luk,
            hp,
            mp,
            watk,
            matk,
            wdef,
            mdef,
            acc,
            avoid,
            speed,
            jump);
}
