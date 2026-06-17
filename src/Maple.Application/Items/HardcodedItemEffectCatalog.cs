using Maple.Core.Items;

namespace Maple.Application.Items;

public sealed class HardcodedItemEffectCatalog : IItemEffectCatalog
{
    public ItemEffect? GetEffect(int itemId)
        => itemId switch
        {
            2000000 => new ItemEffect(itemId, Hp: 50, Mp: 0, HpRate: 0, MpRate: 0),
            2000001 => new ItemEffect(itemId, Hp: 150, Mp: 0, HpRate: 0, MpRate: 0),
            2000002 => new ItemEffect(itemId, Hp: 300, Mp: 0, HpRate: 0, MpRate: 0),
            2000003 => new ItemEffect(itemId, Hp: 0, Mp: 100, HpRate: 0, MpRate: 0),
            2000006 => new ItemEffect(itemId, Hp: 0, Mp: 300, HpRate: 0, MpRate: 0),
            2001000 => new ItemEffect(itemId, Hp: 0, Mp: 0, HpRate: 50, MpRate: 50),
            2001001 => new ItemEffect(itemId, Hp: 0, Mp: 0, HpRate: 100, MpRate: 100),
            >= 2000000 and <= 2099999 => new ItemEffect(itemId, Hp: 100, Mp: 0, HpRate: 0, MpRate: 0),
            _ => null,
        };
}
