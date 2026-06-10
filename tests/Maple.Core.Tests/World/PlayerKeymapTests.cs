using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

public sealed class PlayerKeymapTests
{
    [Fact]
    public void ChangeKeyBinding_MutatesCharacterKeymap()
    {
        var player = new Player(new Character { Id = 1, Name = "KeyUser" }, new Position(0, 0, 0, 0));

        player.ChangeKeyBinding(3, type: 4, action: 12);

        var binding = Assert.Single(player.Character.Keymap);
        Assert.Equal(3, binding.Key);
        Assert.Equal((byte)4, binding.Type);
        Assert.Equal(12, binding.Action);
    }
}
