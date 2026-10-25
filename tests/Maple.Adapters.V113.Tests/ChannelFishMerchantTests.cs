using Maple.Adapters.V113.Channel;

namespace Maple.Adapters.V113.Tests;

/// <summary>P153：精靈小釣手（Java USE_FISH_MERCHANT 0x39 → dropMessage(5, "精靈小釣手尚未開放。")）。</summary>
public sealed class ChannelFishMerchantTests
{
    [Fact]
    public void Opcode_MatchesRecvProperties() => Assert.Equal(0x39, V113ChannelRecvOp.UseFishMerchant);

    [Fact]
    public void Reply_IsChatNoticeWithJavaMessage()
        => Assert.Equal(
            V113BroadcastPackets.ChatNotice("精靈小釣手尚未開放。"),
            V113BroadcastPackets.ChatNotice(V113ChannelConnectionHandler.FishMerchantClosedMessage));
}
