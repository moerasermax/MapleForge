using Maple.Core.Characters;

namespace Maple.Core.Tests.Characters;

public sealed class CharacterMonsterBookCoverTests
{
    [Fact]
    public void ChangeMonsterBookCover_StoresCoverItemId()
    {
        var character = new Character();

        character.ChangeMonsterBookCover(2380001);

        Assert.Equal(2380001, character.MonsterBookCover);
    }

    [Fact]
    public void ChangeMonsterBookCover_AllowsClearingCover()
    {
        var character = new Character { MonsterBookCover = 2380001 };

        character.ChangeMonsterBookCover(0);

        Assert.Equal(0, character.MonsterBookCover);
    }

    [Fact]
    public void ChangeMonsterBookCover_RejectsNegativeCover()
    {
        var character = new Character();

        Assert.Throws<ArgumentOutOfRangeException>(() => character.ChangeMonsterBookCover(-1));
    }
}
