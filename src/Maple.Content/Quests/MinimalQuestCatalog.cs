using Maple.Core.Quests;

namespace Maple.Content.Quests;

/// <summary>
/// Temporary quest catalog for the port branch while Quest.wz parsing is incomplete.
/// IDs are real quests referenced by the OdinMS v113 Java template.
/// </summary>
public sealed class MinimalQuestCatalog : IQuestCatalog
{
    private readonly IReadOnlyDictionary<int, QuestDefinition> _quests;

    public MinimalQuestCatalog()
    {
        var ids = new[]
        {
            20000, 20010, 20015, 20020, 20022,
            10370, 10371, 10372,
        };

        _quests = ids.ToDictionary(id => id, id => QuestDefinition.Empty(id) with
        {
            Name = $"OdinMS referenced quest {id}",
        });
    }

    public QuestDefinition? GetQuest(int questId)
        => _quests.TryGetValue(questId, out var quest) ? quest : null;
}
