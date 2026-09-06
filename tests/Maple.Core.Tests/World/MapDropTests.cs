using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

/// <summary>
/// P061：<see cref="MapDrop.ShouldExpire"/> 對照 Java <c>MapleMapItem.shouldExpire</c>
/// （<c>!pickedUp &amp;&amp; nextExpiry &gt; 0 &amp;&amp; nextExpiry &lt; now</c>，固定 120 秒）。
/// </summary>
public sealed class MapDropTests
{
    private static readonly DateTimeOffset SpawnTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldExpire_BeforeThreshold_ReturnsFalse()
    {
        var drop = NewDrop();

        Assert.False(drop.ShouldExpire(SpawnTime + MapDrop.ExpireAfter - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ShouldExpire_AtOrAfterThreshold_ReturnsTrue()
    {
        var drop = NewDrop();

        Assert.True(drop.ShouldExpire(SpawnTime + MapDrop.ExpireAfter));
        Assert.True(drop.ShouldExpire(SpawnTime + MapDrop.ExpireAfter + TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void ShouldExpire_AlreadyPickedUp_ReturnsFalse()
    {
        var drop = NewDrop();
        drop.TryMarkPickedUp();

        Assert.False(drop.ShouldExpire(SpawnTime + MapDrop.ExpireAfter + TimeSpan.FromMinutes(5)));
    }

    private static MapDrop NewDrop() => MapDrop.ForItem(
        1_000_000,
        new Item { ItemId = 4000000, Quantity = 1 },
        new Position(0, 0, 0, 0),
        new Position(0, 0, 0, 0),
        sourceObjectId: 1,
        ownerId: 1,
        dropType: 0,
        spawnedAt: SpawnTime);
}
