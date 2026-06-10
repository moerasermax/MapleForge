using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

public sealed class PlayerItemEffectTests
{
    [Fact]
    public void UseItemEffect_TracksRuntimeItemEffect()
    {
        var player = new Player(new Character { Id = 1, Name = "EffectUser" }, new Position(0, 0, 0, 0));

        player.UseItemEffect(5010000);

        Assert.Equal(5010000, player.ItemEffectItemId);
    }

    [Fact]
    public void CancelItemEffect_ClearsRuntimeItemEffect()
    {
        var player = new Player(new Character { Id = 1, Name = "EffectUser" }, new Position(0, 0, 0, 0));

        player.UseItemEffect(5010000);
        player.CancelItemEffect();

        Assert.Equal(0, player.ItemEffectItemId);
    }
}
