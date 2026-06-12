using Maple.Core.Trade;

namespace Maple.Core.World;

public sealed partial class Player
{
    public MapleTrade? Trade { get; private set; }

    public bool IsTrading => Trade is not null;

    public void AttachTrade(MapleTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);

        if (Trade is not null && !ReferenceEquals(Trade, trade))
        {
            throw new InvalidOperationException("Player is already in another trade.");
        }

        Trade = trade;
    }

    public void ClearTrade(MapleTrade trade)
    {
        if (ReferenceEquals(Trade, trade))
        {
            Trade = null;
        }
    }
}
