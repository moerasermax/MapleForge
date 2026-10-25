using Maple.Adapters.V113.Channel;

namespace Maple.Adapters.V113.Tests;

/// <summary>P194：教學 UI 封包（Java UIPacket 版型；Java-source candidate／unverified）。</summary>
public sealed class ChannelUiPacketsTests
{
    [Fact]
    public void SummonAndIntroPackets_MatchJavaLayout()
    {
        Assert.Equal(new byte[] { 0xD8, 0x00, 1 }, V113UiPackets.SummonHelper(true));
        Assert.Equal(new byte[] { 0xD9, 0x00, 1, 3, 0, 0, 0, 0x58, 0x1B, 0, 0 }, V113UiPackets.SummonMessage(3));
        Assert.Equal(new byte[] { 0xD9, 0x00, 0, 1, 0, (byte)'a', 200, 0, 0, 0, 0, 0, 0x10, 0x27, 0, 0 }, V113UiPackets.SummonMessage("a"));
        Assert.Equal(new byte[] { 0xD6, 0x00, 0 }, V113UiPackets.IntroLock(false));
        Assert.Equal(new byte[] { 0xD7, 0x00, 1 }, V113UiPackets.IntroDisableUi(true));
        Assert.Equal(new byte[] { 0xC7, 0x00, 0x15, 1, 0, (byte)'x', 1, 0, 0, 0 }, V113UiPackets.AranTutorialBalloon("x"));
        Assert.Equal(new byte[] { 0x3C, 0x01, 8, 0, 0, 0, 0, 1, 1, 1, 1, 0, (byte)'e' }, V113UiPackets.EvanTutorial("e"));
    }
}
