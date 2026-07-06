using System.Collections.Concurrent;
using Maple.Core.PlayerShops;

namespace Maple.Adapters.V113.Channel;

public interface IHiredMerchantSessionDispatcher
{
    void Register(int storeId, int characterId, Func<byte[], CancellationToken, Task> sendPacket);

    void Deregister(int storeId, int characterId);

    void Clear(int storeId);

    Task SendToParticipantsAsync(
        HiredMerchant merchant,
        byte[] packet,
        CancellationToken cancellationToken,
        int? exceptCharacterId = null);
}

public sealed class InMemoryHiredMerchantSessionDispatcher : IHiredMerchantSessionDispatcher
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, Func<byte[], CancellationToken, Task>>> _sessions = new();

    public void Register(int storeId, int characterId, Func<byte[], CancellationToken, Task> sendPacket)
    {
        ArgumentNullException.ThrowIfNull(sendPacket);
        var store = _sessions.GetOrAdd(storeId, static _ => new ConcurrentDictionary<int, Func<byte[], CancellationToken, Task>>());
        store[characterId] = sendPacket;
    }

    public void Deregister(int storeId, int characterId)
    {
        if (_sessions.TryGetValue(storeId, out var store))
        {
            store.TryRemove(characterId, out _);
        }
    }

    public void Clear(int storeId)
    {
        _sessions.TryRemove(storeId, out _);
    }

    public async Task SendToParticipantsAsync(
        HiredMerchant merchant,
        byte[] packet,
        CancellationToken cancellationToken,
        int? exceptCharacterId = null)
    {
        if (!_sessions.TryGetValue(merchant.StoreId, out var store))
        {
            return;
        }

        var recipients = new HashSet<int> { merchant.OwnerId };
        foreach (var visitor in merchant.State.Visitors)
        {
            recipients.Add(visitor.CharacterId);
        }

        foreach (var characterId in recipients)
        {
            if (exceptCharacterId == characterId || !store.TryGetValue(characterId, out var send))
            {
                continue;
            }

            try
            {
                await send(packet, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                store.TryRemove(characterId, out _);
            }
        }
    }
}
