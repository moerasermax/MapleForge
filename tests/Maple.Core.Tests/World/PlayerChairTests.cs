using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

public sealed class PlayerChairTests
{
    [Fact]
    public void UseChair_TracksRuntimeChairWithoutCharacterPersistenceField()
    {
        var player = new Player(new Character { Id = 1, Name = "ChairUser" }, new Position(0, 0, 0, 0));

        player.UseChair(3010001);

        Assert.Equal(3010001, player.ChairItemId);
    }

    [Fact]
    public void CancelChair_ClearsRuntimeChair()
    {
        var player = new Player(new Character { Id = 1, Name = "ChairUser" }, new Position(0, 0, 0, 0));

        player.UseChair(3010001);
        player.CancelChair();

        Assert.Equal(0, player.ChairItemId);
    }

    [Fact]
    public void UseMapChair_TracksMapChairId()
    {
        var player = new Player(new Character { Id = 1, Name = "ChairUser" }, new Position(0, 0, 0, 0));

        player.UseMapChair(7);

        Assert.Equal(7, player.ChairItemId);
    }
}
