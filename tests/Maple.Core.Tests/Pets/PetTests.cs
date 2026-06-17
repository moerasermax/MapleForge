using Maple.Core.Pets;
using Maple.Core.World;

namespace Maple.Core.Tests.Pets;

public sealed class PetTests
{
    [Fact]
    public void Feed_IncreasesFullnessAndCloseness()
    {
        var pet = CreatePet(level: 2, closeness: 1, fullness: 50);

        var result = pet.Feed(gainCloseness: true);

        Assert.True(result.Success);
        Assert.True(result.FullnessIncreased);
        Assert.True(result.ClosenessChanged);
        Assert.Equal((byte)80, pet.Fullness);
        Assert.Equal((short)2, pet.Closeness);
        Assert.Equal((byte)2, pet.Level);
    }

    [Fact]
    public void Feed_WhenFull_DecreasesClosenessAndCanLevelDown()
    {
        var pet = CreatePet(level: 2, closeness: 1, fullness: 100);

        var result = pet.Feed(gainCloseness: true);

        Assert.True(result.Success);
        Assert.False(result.FullnessIncreased);
        Assert.True(result.ClosenessChanged);
        Assert.True(result.LevelChanged);
        Assert.Equal((byte)100, pet.Fullness);
        Assert.Equal((short)0, pet.Closeness);
        Assert.Equal((byte)1, pet.Level);
    }

    [Fact]
    public void ExecuteCommand_WhenSuccessful_IncreasesClosenessAndCanLevelUp()
    {
        var pet = CreatePet(level: 2, closeness: 1, fullness: 100);

        var result = pet.ExecuteCommand(probability: 50, increase: 2, roll: 0);

        Assert.True(result.Success);
        Assert.True(result.ClosenessChanged);
        Assert.True(result.LevelChanged);
        Assert.Equal((short)3, pet.Closeness);
        Assert.Equal((byte)3, pet.Level);
    }

    [Fact]
    public void ExecuteCommand_WhenFailed_DoesNotChangeCloseness()
    {
        var pet = CreatePet(level: 2, closeness: 1, fullness: 100);

        var result = pet.ExecuteCommand(probability: 50, increase: 2, roll: 98);

        Assert.False(result.Success);
        Assert.False(result.ClosenessChanged);
        Assert.False(result.LevelChanged);
        Assert.Equal((short)1, pet.Closeness);
        Assert.Equal((byte)2, pet.Level);
    }

    private static Pet CreatePet(byte level, short closeness, byte fullness)
        => new(
            petId: 1001,
            itemId: 5000000,
            name: "Kitty",
            level,
            closeness,
            fullness,
            flags: 0,
            position: new Position(10, 20, 0, 7));
}
