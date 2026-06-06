using Maple.Core.World;

namespace Maple.Application.Maps;

public sealed class InMemoryFieldInstanceRegistry : IFieldInstanceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<int, FieldInstance> _fields = new();

    public FieldInstance GetOrCreate(int mapId, out bool created)
    {
        lock (_gate)
        {
            if (_fields.TryGetValue(mapId, out var field))
            {
                created = false;
                return field;
            }

            field = new FieldInstance(mapId);
            _fields.Add(mapId, field);
            created = true;
            return field;
        }
    }
}

