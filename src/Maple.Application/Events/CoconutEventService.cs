using System.Collections.Concurrent;
using Maple.Core.Events;

namespace Maple.Application.Events;

public sealed class CoconutEventService
{
    private readonly ConcurrentDictionary<int, CoconutEvent> _events = new();

    public CoconutHitResult Hit(int mapId, short coconutId, CoconutTeam team)
    {
        var coconutEvent = _events.GetOrAdd(mapId, _ => CoconutEvent.CreateRunning());
        lock (coconutEvent)
        {
            return coconutEvent.Hit(coconutId, team, Random.Shared.NextDouble());
        }
    }

    public CoconutEvent GetOrCreateRunning(int mapId) => _events.GetOrAdd(mapId, _ => CoconutEvent.CreateRunning());

    public void Stop(int mapId)
    {
        if (_events.TryGetValue(mapId, out var coconutEvent))
        {
            lock (coconutEvent)
            {
                coconutEvent.Stop();
            }
        }
    }
}
