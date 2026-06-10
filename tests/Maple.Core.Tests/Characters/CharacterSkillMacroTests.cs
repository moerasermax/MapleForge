using Maple.Core.Characters;

namespace Maple.Core.Tests.Characters;

public sealed class CharacterSkillMacroTests
{
    [Fact]
    public void UpdateSkillMacro_AddsOrUpdatesMacro()
    {
        var character = new Character();

        character.UpdateSkillMacro(0, "boss", shout: 1, skill1: 100, skill2: 101, skill3: 102);
        character.UpdateSkillMacro(0, "mob", shout: 0, skill1: 200, skill2: 201, skill3: 202);

        var macro = Assert.Single(character.SkillMacros);
        Assert.Equal(0, macro.Position);
        Assert.Equal("mob", macro.Name);
        Assert.Equal((byte)0, macro.Shout);
        Assert.Equal(200, macro.Skill1);
        Assert.Equal(201, macro.Skill2);
        Assert.Equal(202, macro.Skill3);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void UpdateSkillMacro_RejectsOutOfRangePosition(int position)
    {
        var character = new Character();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            character.UpdateSkillMacro(position, "bad", 0, 1, 2, 3));
    }
}
