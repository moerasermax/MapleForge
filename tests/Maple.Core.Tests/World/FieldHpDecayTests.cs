using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

/// <summary>P074：<see cref="FieldHpDecay"/> 對照 Java <c>MapleMap.setHPDec</c>/<c>canHurt()</c>。</summary>
public sealed class FieldHpDecayTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_NoDecHp_ReturnsNull()
    {
        Assert.Null(FieldHpDecay.Create(new MapData { MapId = 100000000 }, Start));
    }

    [Fact]
    public void Create_MiniDungeonWithoutDecHp_StillStartsTimer()
    {
        var decay = FieldHpDecay.Create(new MapData { MapId = FieldHpDecay.MiniDungeonMapId }, Start);

        Assert.NotNull(decay);
        Assert.Equal(0, decay!.DecHp);
    }

    [Fact]
    public void TryHurt_FiresOnlyAfterIntervalStrictlyPassed_ThenResets()
    {
        var decay = FieldHpDecay.Create(
            new MapData { MapId = 211040000, DecHp = 20, DecHpInterval = 10_000, ProtectItem = 1072000 },
            Start)!;

        Assert.Equal(20, decay.DecHp);
        Assert.Equal(1072000, decay.ProtectItemId);
        Assert.False(decay.TryHurt(Start.AddSeconds(10)));       // 剛好等於週期：Java 用 <，不觸發
        Assert.True(decay.TryHurt(Start.AddSeconds(11)));
        Assert.Equal(Start.AddSeconds(11), decay.LastHurtAt);
        Assert.False(decay.TryHurt(Start.AddSeconds(15)));       // 從 11 秒重新計時
        Assert.True(decay.TryHurt(Start.AddSeconds(22)));
    }
}
