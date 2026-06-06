using Maple.Core.Data;
using Maple.Core.Quests;

namespace Maple.Content.Quests;

public sealed class WzQuestCatalog : IQuestCatalog
{
    private readonly IDataProvider _data;
    private readonly object _gate = new();
    private IReadOnlyDictionary<int, QuestDefinition>? _quests;

    public WzQuestCatalog(IDataProvider data)
    {
        _data = data;
    }

    public QuestDefinition? GetQuest(int questId)
    {
        var quests = EnsureLoaded();
        return quests.TryGetValue(questId, out var quest) ? quest : null;
    }

    private IReadOnlyDictionary<int, QuestDefinition> EnsureLoaded()
    {
        if (_quests is not null)
        {
            return _quests;
        }

        lock (_gate)
        {
            _quests ??= LoadAll();
            return _quests;
        }
    }

    private IReadOnlyDictionary<int, QuestDefinition> LoadAll()
    {
        var infoRoot = _data.GetAt("Quest", "QuestInfo.img");
        var checkRoot = _data.GetAt("Quest", "Check.img");
        var actRoot = _data.GetAt("Quest", "Act.img");
        var ids = new SortedSet<int>();

        AddQuestIds(ids, infoRoot);
        AddQuestIds(ids, checkRoot);
        AddQuestIds(ids, actRoot);

        var quests = new Dictionary<int, QuestDefinition>(ids.Count);
        foreach (var id in ids)
        {
            quests[id] = LoadQuest(id, infoRoot?[id.ToString()], checkRoot?[id.ToString()], actRoot?[id.ToString()]);
        }

        return quests;
    }

    private static QuestDefinition LoadQuest(int id, IDataNode? info, IDataNode? check, IDataNode? act)
    {
        var startRequirements = ParseRequirements(check?["0"]);
        var completeRequirements = ParseRequirements(check?["1"]);
        var startActions = ParseActions(act?["0"]);
        var completeActions = ParseActions(act?["1"]);
        var allRequirements = startRequirements.Concat(completeRequirements).ToArray();
        var relevantMobs = allRequirements
            .Where(r => r.Kind == QuestRequirementKind.Mob)
            .SelectMany(r => r.Values ?? Array.Empty<QuestIntPair>())
            .GroupBy(v => v.Id)
            .ToDictionary(g => g.Key, g => g.Max(v => v.Value));

        var normalAutoStart = HasRequirement(check?["0"], "normalAutoStart") || HasRequirement(check?["1"], "normalAutoStart");
        var repeatable = allRequirements.Any(r => r.Kind == QuestRequirementKind.Interval) || normalAutoStart;

        return new QuestDefinition(
            id,
            GetString(info, "name"),
            AutoStart: GetInt(info, "autoStart", 0) > 0 || normalAutoStart,
            AutoPreComplete: GetInt(info, "autoPreComplete", 0) > 0,
            AutoAccept: GetInt(info, "autoAccept", 0) > 0,
            AutoComplete: GetInt(info, "autoComplete", 0) > 0,
            Repeatable: repeatable,
            ScriptedStart: HasRequirement(check?["0"], "startscript"),
            ScriptedEnd: HasRequirement(check?["1"], "endscript"),
            Blocked: GetInt(info, "blocked", 0) > 0,
            ViewMedalItem: GetInt(info, "viewMedalItem", 0),
            SelectedSkillId: GetInt(info, "selectedSkillID", 0),
            StartRequirements: startRequirements,
            CompleteRequirements: completeRequirements,
            StartActions: startActions,
            CompleteActions: completeActions,
            RelevantMobs: relevantMobs);
    }

    private static IReadOnlyList<QuestRequirement> ParseRequirements(IDataNode? node)
    {
        if (node is null)
        {
            return Array.Empty<QuestRequirement>();
        }

        var requirements = new List<QuestRequirement>();
        foreach (var child in node.Children.Values)
        {
            var kind = RequirementKindOf(child.Name);
            var req = kind switch
            {
                QuestRequirementKind.Job => new QuestRequirement(kind, Values: ReadIndexedIntValues(child)),
                QuestRequirementKind.Item => new QuestRequirement(kind, Values: ReadIdCountValues(child, "count")),
                QuestRequirementKind.Quest => new QuestRequirement(kind, Values: ReadIdCountValues(child, "state")),
                QuestRequirementKind.Mob => new QuestRequirement(kind, Values: ReadIdCountValues(child, "count")),
                QuestRequirementKind.Skill => new QuestRequirement(kind, Values: ReadIdCountValues(child, "acquire")),
                QuestRequirementKind.LevelMin or QuestRequirementKind.LevelMax or QuestRequirementKind.Npc
                    or QuestRequirementKind.Interval or QuestRequirementKind.QuestComplete or QuestRequirementKind.Pop =>
                    new QuestRequirement(kind, IntValue: GetIntValue(child, -1)),
                QuestRequirementKind.FieldEnter => new QuestRequirement(kind, IntValue: GetIntValue(child["0"], -1)),
                QuestRequirementKind.End => new QuestRequirement(kind, StringValue: GetStringValue(child)),
                _ => new QuestRequirement(kind),
            };

            requirements.Add(req);
        }

        return requirements;
    }

    private static IReadOnlyList<QuestAction> ParseActions(IDataNode? node)
    {
        if (node is null)
        {
            return Array.Empty<QuestAction>();
        }

        var actions = new List<QuestAction>();
        foreach (var child in node.Children.Values)
        {
            var kind = ActionKindOf(child.Name);
            var action = kind switch
            {
                QuestActionKind.Item => new QuestAction(kind, Items: ReadQuestItems(child)),
                QuestActionKind.Quest => new QuestAction(kind, QuestStates: ReadIdCountValues(child, "state")),
                QuestActionKind.Exp or QuestActionKind.Money or QuestActionKind.NextQuest or QuestActionKind.Pop
                    or QuestActionKind.BuffItemId or QuestActionKind.InfoNumber or QuestActionKind.Sp =>
                    new QuestAction(kind, IntValue: GetIntValue(child, 0)),
                _ => new QuestAction(kind),
            };

            actions.Add(action);
        }

        return actions;
    }

    private static IReadOnlyList<QuestItemAction> ReadQuestItems(IDataNode node)
    {
        var items = new List<QuestItemAction>();
        foreach (var child in OrderedChildren(node))
        {
            var itemId = GetInt(child, "id", 0);
            if (itemId <= 0)
            {
                continue;
            }

            items.Add(new QuestItemAction(
                itemId,
                GetInt(child, "count", 0),
                GetInt(child, "period", 0),
                GetInt(child, "gender", 2),
                GetInt(child, "job", 0),
                GetInt(child, "jobEx", 0),
                GetInt(child, "prop", -2)));
        }

        return items;
    }

    private static IReadOnlyList<QuestIntPair> ReadIndexedIntValues(IDataNode node)
    {
        var values = new List<QuestIntPair>();
        var index = 0;
        foreach (var child in OrderedChildren(node))
        {
            values.Add(new QuestIntPair(index++, GetIntValue(child, -1)));
        }

        return values;
    }

    private static IReadOnlyList<QuestIntPair> ReadIdCountValues(IDataNode node, string valueKey)
    {
        var values = new List<QuestIntPair>();
        foreach (var child in OrderedChildren(node))
        {
            var id = GetInt(child, "id", 0);
            if (id <= 0)
            {
                continue;
            }

            values.Add(new QuestIntPair(id, GetInt(child, valueKey, 0)));
        }

        return values;
    }

    private static QuestRequirementKind RequirementKindOf(string name)
        => name switch
        {
            "job" => QuestRequirementKind.Job,
            "item" => QuestRequirementKind.Item,
            "quest" => QuestRequirementKind.Quest,
            "lvmin" => QuestRequirementKind.LevelMin,
            "lvmax" => QuestRequirementKind.LevelMax,
            "end" => QuestRequirementKind.End,
            "mob" => QuestRequirementKind.Mob,
            "npc" => QuestRequirementKind.Npc,
            "fieldEnter" => QuestRequirementKind.FieldEnter,
            "interval" => QuestRequirementKind.Interval,
            "questComplete" => QuestRequirementKind.QuestComplete,
            "pop" => QuestRequirementKind.Pop,
            "skill" => QuestRequirementKind.Skill,
            _ => QuestRequirementKind.Unknown,
        };

    private static QuestActionKind ActionKindOf(string name)
        => name switch
        {
            "exp" => QuestActionKind.Exp,
            "item" => QuestActionKind.Item,
            "nextQuest" => QuestActionKind.NextQuest,
            "money" => QuestActionKind.Money,
            "quest" => QuestActionKind.Quest,
            "skill" => QuestActionKind.Skill,
            "pop" => QuestActionKind.Pop,
            "buffItemID" => QuestActionKind.BuffItemId,
            "infoNumber" => QuestActionKind.InfoNumber,
            "sp" => QuestActionKind.Sp,
            _ => QuestActionKind.Unknown,
        };

    private static bool HasRequirement(IDataNode? node, string name)
        => node?[name] is not null;

    private static void AddQuestIds(ISet<int> ids, IDataNode? node)
    {
        if (node is null)
        {
            return;
        }

        foreach (var key in node.Children.Keys)
        {
            if (int.TryParse(key, out var id))
            {
                ids.Add(id);
            }
        }
    }

    private static IEnumerable<IDataNode> OrderedChildren(IDataNode node)
        => node.Children
            .OrderBy(kv => int.TryParse(kv.Key, out var n) ? n : int.MaxValue)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value);

    private static int GetInt(IDataNode? node, string key, int defaultValue)
        => GetIntValue(node?[key], defaultValue);

    private static int GetIntValue(IDataNode? node, int defaultValue)
        => node?.Value switch
        {
            int v => v,
            short v => v,
            long v when v >= int.MinValue && v <= int.MaxValue => (int)v,
            byte v => v,
            sbyte v => v,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => defaultValue,
        };

    private static string GetString(IDataNode? node, string key)
        => GetStringValue(node?[key]);

    private static string GetStringValue(IDataNode? node)
        => node?.Value is string s ? s : string.Empty;
}
