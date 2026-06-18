using System.Collections.Concurrent;
using Maple.Adapters.V113.Channel;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelCashShopTransitionTests
{
    [Fact]
    public void CanEnterCashShopFromMap_RejectsMapleLand()
    {
        Assert.False(V113ChannelConnectionHandler.CanEnterCashShopFromMap(0));
        Assert.False(V113ChannelConnectionHandler.CanEnterCashShopFromMap(1010003));
        Assert.True(V113ChannelConnectionHandler.CanEnterCashShopFromMap(1010004));
    }

    [Fact]
    public void CanEnterCashShopFromMap_RejectsPapulatusClocktowerMap()
    {
        Assert.False(V113ChannelConnectionHandler.CanEnterCashShopFromMap(220080001));
        Assert.True(V113ChannelConnectionHandler.CanEnterCashShopFromMap(100000000));
    }

    [Fact]
    public void RegisterCashShopTransition_StoresPreviousMapAndChannel()
    {
        var pending = new ConcurrentDictionary<int, CashShopTransitionData>();

        var transition = V113ChannelConnectionHandler.RegisterCashShopTransition(
            pending,
            characterId: 42,
            previousMapId: 100000000,
            previousChannel: 2);

        Assert.True(pending.TryGetValue(42, out var stored));
        Assert.Equal(42, transition.CharacterId);
        Assert.Equal(100000000, stored.PreviousMapId);
        Assert.Equal(2, stored.PreviousChannel);
        Assert.True(stored.RegisteredAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void TryConsumeCashShopTransition_RemovesTransitionOnlyOnce()
    {
        var pending = new ConcurrentDictionary<int, CashShopTransitionData>();
        V113ChannelConnectionHandler.RegisterCashShopTransition(pending, 42, 100000000, 0);

        var first = V113ChannelConnectionHandler.TryConsumeCashShopTransition(pending, 42, out var transition);
        var second = V113ChannelConnectionHandler.TryConsumeCashShopTransition(pending, 42, out _);

        Assert.True(first);
        Assert.Equal(100000000, transition.PreviousMapId);
        Assert.False(second);
        Assert.Empty(pending);
    }

    [Fact]
    public void TryConsumeCashShopTransition_StaleTransitionReturnsFalseAndRemovesEntry()
    {
        var pending = new ConcurrentDictionary<int, CashShopTransitionData>();
        pending[42] = new CashShopTransitionData(
            CharacterId: 42,
            PreviousMapId: 100000000,
            PreviousChannel: 0,
            RegisteredAt: DateTimeOffset.UtcNow - TimeSpan.FromSeconds(31));

        var consumed = V113ChannelConnectionHandler.TryConsumeCashShopTransition(pending, 42, out var transition);

        Assert.False(consumed);
        Assert.Null(transition);
        Assert.Empty(pending);
    }

    [Fact]
    public void LeaveCashShop_UsesChangeMapRequestAndChannelChangeReconnect()
    {
        var reconnect = V113ChannelChangePackets.ChangeChannel([127, 0, 0, 1], 8585);

        Assert.Equal(0x1E, V113ChannelRecvOp.ChangeMap);
        Assert.Equal(V113ChannelSendOp.ChangeChannel, BitConverter.ToInt16(reconnect, 0));
        Assert.Equal((byte)1, reconnect[2]);
        Assert.Equal((short)8585, BitConverter.ToInt16(reconnect, 7));
    }
}
