using Maple.Core.Maps;

namespace Maple.Core.Tests.Maps;

public sealed class FieldLimitTypeTests
{
    [Fact]
    public void VipRock_UsesJavaReferenceValue()
    {
        Assert.Equal(0x40L, (long)FieldLimitType.VipRock);
    }

    [Fact]
    public void Check_ReturnsTrue_WhenVipRockFlagIsSet()
    {
        var fieldLimit = (long)FieldLimitType.VipRock;

        Assert.True(FieldLimitType.VipRock.Check(fieldLimit));
    }

    [Fact]
    public void Check_ReturnsFalse_WhenVipRockFlagIsNotSet()
    {
        var fieldLimit = (long)FieldLimitType.Minigames;

        Assert.False(FieldLimitType.VipRock.Check(fieldLimit));
    }

    [Fact]
    public void Check_ReturnsTrue_WhenAnyRequestedFlagIsPresentInCombinedMask()
    {
        var requestedFlags = FieldLimitType.VipRock | FieldLimitType.ChannelSwitch;
        var fieldLimit = (long)(FieldLimitType.Minigames | FieldLimitType.VipRock);

        Assert.True(requestedFlags.Check(fieldLimit));
    }
}
