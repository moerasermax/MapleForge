using System.Collections.Concurrent;
using System.Globalization;
using Maple.Core.Data;
using Maple.Core.World;

namespace Maple.Application.Mobs;

/// <summary>
/// P122：怪物技能資料（Skill.wz MobSkill.img/{id}/level/{lv}）。對照 Java <c>DumpMobSkills</c> 的欄位與預設值
/// （hp 100、mpCon 0、x 1、y 1、time 0、prop 100、limit 0、summonEffect 0、interval 0、summonOnce 0、0..n 召喚怪物）。
/// </summary>
public sealed class WzMobSkillCatalog : IMobSkillCatalog
{
    private readonly IDataProvider _data;
    private readonly ConcurrentDictionary<(int, int), MobSkillDefinition?> _cache = new();

    public WzMobSkillCatalog(IDataProvider data)
    {
        _data = data;
    }

    public MobSkillDefinition? Get(int skillId, int level)
        => _cache.GetOrAdd((skillId, level), static (key, data) => Load(data, key.Item1, key.Item2), _data);

    private static MobSkillDefinition? Load(IDataProvider data, int skillId, int level)
    {
        IDataNode? node;
        try
        {
            node = data.GetAt("Skill", $"MobSkill.img/{skillId}/level/{level}");
        }
        catch (InvalidDataException)
        {
            return null;
        }

        if (node is null)
        {
            return null;
        }

        var summons = new List<int>();
        for (var i = 0; node[i.ToString(CultureInfo.InvariantCulture)] is { } summon; i++)
        {
            summons.Add(ToInt(summon.Value) ?? 0);
        }

        var lt = node["lt"]?.Value as DataVector;
        var rb = node["rb"]?.Value as DataVector;
        var area = lt is not null && rb is not null && (lt.X != 0 || lt.Y != 0 || rb.X != 0 || rb.Y != 0)
            ? new MobSkillArea((short)lt.X, (short)lt.Y, (short)rb.X, (short)rb.Y)
            : null;

        return new MobSkillDefinition(
            skillId,
            level,
            Hp: GetInt(node, "hp", 100),
            MpCon: GetInt(node, "mpCon", 0),
            X: GetInt(node, "x", 1),
            Y: GetInt(node, "y", 1),
            DurationMilliseconds: GetInt(node, "time", 0) * 1000,
            CoolTimeMilliseconds: GetInt(node, "interval", 0) * 1000,
            Prop: GetInt(node, "prop", 100) / 100.0,
            Limit: GetInt(node, "limit", 0),
            SpawnEffect: GetInt(node, "summonEffect", 0),
            Summons: summons,
            Area: area,
            SummonOnce: GetInt(node, "summonOnce", 0) > 0);
    }

    private static int GetInt(IDataNode node, string key, int defaultValue)
        => ToInt(node[key]?.Value) ?? defaultValue;

    private static int? ToInt(object? value) => value switch
    {
        int v => v,
        short v => v,
        long v when v is >= int.MinValue and <= int.MaxValue => (int)v,
        byte v => v,
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };
}
