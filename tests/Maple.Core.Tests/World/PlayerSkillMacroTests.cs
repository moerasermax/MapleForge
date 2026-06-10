using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

public sealed class PlayerSkillMacroTests
{
    [Fact]
    public void UpdateSkillMacro_MutatesCharacterMacros()
    {
        var player = new Player(new Character { Id = 1, Name = "MacroUser" }, new Position(0, 0, 0, 0));

        player.UpdateSkillMacro(1, "combo", shout: 1, skill1: 1001000, skill2: 1001001, skill3: 1001002);

        var macro = Assert.Single(player.Character.SkillMacros);
        Assert.Equal(1, macro.Position);
        Assert.Equal("combo", macro.Name);
        Assert.Equal((byte)1, macro.Shout);
        Assert.Equal(1001000, macro.Skill1);
        Assert.Equal(1001001, macro.Skill2);
        Assert.Equal(1001002, macro.Skill3);
    }
}
