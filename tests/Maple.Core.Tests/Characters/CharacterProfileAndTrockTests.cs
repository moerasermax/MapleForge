using Maple.Core.Characters;

namespace Maple.Core.Tests.Characters;

public sealed class CharacterProfileAndTrockTests
{
    [Fact]
    public void UpdateProfileFields_StoresCharacterInfoValues()
    {
        var character = new Character();

        character.UpdateCharacterMessage("ready");
        character.UpdateProfileExpression(4);
        character.UpdateProfileBirthday(blood: 1, month: 6, day: 10, constellation: 9);

        Assert.Equal("ready", character.CharacterMessage);
        Assert.Equal((byte)4, character.ProfileExpression);
        Assert.Equal((byte)1, character.Blood);
        Assert.Equal((byte)6, character.BirthMonth);
        Assert.Equal((byte)10, character.BirthDay);
        Assert.Equal((byte)9, character.Constellation);
    }

    [Fact]
    public void UpdatePetAutoPot_StoresHpAndMpItemIds()
    {
        var character = new Character();

        Assert.True(character.UpdatePetAutoPot(1, 2000000));
        Assert.True(character.UpdatePetAutoPot(2, 2000001));
        Assert.True(character.UpdatePetAutoPot(1, 0));
        Assert.False(character.UpdatePetAutoPot(9, 2000002));

        Assert.Equal(0, character.PetAutoHpItemId);
        Assert.Equal(2000001, character.PetAutoMpItemId);
    }

    [Fact]
    public void RockSlots_NormalizeAndMutateFixedSlotLists()
    {
        var character = new Character
        {
            RegularRocks = [100000000],
            VipRocks = [],
        };

        Assert.True(character.AddRegularRock(101000000));
        Assert.False(character.AddRegularRock(101000000));
        Assert.True(character.RemoveRegularRock(100000000));
        Assert.True(character.AddVipRock(200000000));

        Assert.Equal(
            [
                Character.EmptyRockMapId,
                101000000,
                Character.EmptyRockMapId,
                Character.EmptyRockMapId,
                Character.EmptyRockMapId,
            ],
            character.GetRegularRockSlots());
        Assert.Equal(10, character.GetVipRockSlots().Count);
        Assert.Equal(200000000, character.GetVipRockSlots()[0]);
    }
}
