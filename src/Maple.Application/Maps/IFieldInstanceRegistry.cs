using Maple.Core.World;

namespace Maple.Application.Maps;

/// <summary>Process-local runtime field registry keyed by map id.</summary>
public interface IFieldInstanceRegistry
{
    FieldInstance GetOrCreate(int mapId, out bool created);
}

