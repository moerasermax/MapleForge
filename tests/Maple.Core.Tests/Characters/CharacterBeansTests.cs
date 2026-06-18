using Maple.Core.Characters;

namespace Maple.Core.Tests.Characters;

public sealed class CharacterBeansTests
{
    [Fact]
    public void GainBeans_ClampsAtZero()
    {
        var character = new Character { Beans = 2 };

        character.GainBeans(-5);

        Assert.Equal(0, character.Beans);
    }
}
