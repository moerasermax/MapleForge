using Maple.Core.World;

namespace Maple.Application.Maps;

/// <summary>Process-local runtime field registry keyed by map id.</summary>
public interface IFieldInstanceRegistry
{
    FieldInstance GetOrCreate(int mapId, out bool created);

    /// <summary>P063（M4-2 世界 tick）：目前所有已建立過的 field（供背景排程器逐一巡邏）。</summary>
    IReadOnlyCollection<FieldInstance> All { get; }
}

