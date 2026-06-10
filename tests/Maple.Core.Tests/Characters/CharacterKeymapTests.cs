using Maple.Core.Characters;

namespace Maple.Core.Tests.Characters;

public sealed class CharacterKeymapTests
{
    [Fact]
    public void ChangeKeyBinding_AddsOrUpdatesBinding()
    {
        var character = new Character();

        character.ChangeKeyBinding(2, type: 4, action: 10);
        character.ChangeKeyBinding(2, type: 5, action: 52);

        var binding = Assert.Single(character.Keymap);
        Assert.Equal(2, binding.Key);
        Assert.Equal((byte)5, binding.Type);
        Assert.Equal(52, binding.Action);
    }

    [Fact]
    public void ChangeKeyBinding_TypeZero_RemovesBinding()
    {
        var character = new Character();
        character.ChangeKeyBinding(2, type: 4, action: 10);

        character.ChangeKeyBinding(2, type: 0, action: 0);

        Assert.Empty(character.Keymap);
    }
}
