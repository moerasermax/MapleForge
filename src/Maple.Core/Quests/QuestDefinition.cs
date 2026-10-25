using Maple.Core.Inventory;

namespace Maple.Core.Quests;

public enum QuestRequirementKind
{
    Unknown,
    Job,
    Item,
    Quest,
    LevelMin,
    LevelMax,
    End,
    Mob,
    Npc,
    FieldEnter,
    Interval,
    QuestComplete,
    Pop,
    Skill,
}

public enum QuestActionKind
{
    Unknown,
    Exp,
    Item,
    NextQuest,
    Money,
    Quest,
    Skill,
    Pop,
    BuffItemId,
    InfoNumber,
    Sp,
}

public sealed record QuestIntPair(int Id, int Value);

public sealed record QuestRequirement(
    QuestRequirementKind Kind,
    int IntValue = 0,
    string StringValue = "",
    IReadOnlyList<QuestIntPair>? Values = null);

public sealed record QuestItemAction(
    int ItemId,
    int Count,
    int Period,
    int Gender,
    int Job,
    int JobEx,
    int Prop);

public sealed record QuestAction(
    QuestActionKind Kind,
    int IntValue = 0,
    IReadOnlyList<QuestItemAction>? Items = null,
    IReadOnlyList<QuestIntPair>? QuestStates = null,
    IReadOnlyList<QuestSkillAction>? Skills = null);

/// <summary>P163：Act.img 的 skill 子節點（id、skillLevel、masterLevel、job 清單 = Java applicableJobs）。</summary>
public sealed record QuestSkillAction(int SkillId, int SkillLevel, int MasterLevel, IReadOnlyList<int> Jobs);

public sealed record QuestInventoryMutation(
    InventoryType Type,
    short Slot,
    int ItemId,
    short OldQuantity,
    short NewQuantity)
{
    public bool Removed => NewQuantity <= 0;
}

public sealed record QuestDefinition(
    int Id,
    string Name,
    bool AutoStart,
    bool AutoPreComplete,
    bool AutoAccept,
    bool AutoComplete,
    bool Repeatable,
    bool ScriptedStart,
    bool ScriptedEnd,
    bool Blocked,
    int ViewMedalItem,
    int SelectedSkillId,
    IReadOnlyList<QuestRequirement> StartRequirements,
    IReadOnlyList<QuestRequirement> CompleteRequirements,
    IReadOnlyList<QuestAction> StartActions,
    IReadOnlyList<QuestAction> CompleteActions,
    IReadOnlyDictionary<int, int> RelevantMobs)
{
    public static QuestDefinition Empty(int id) => new(
        id,
        string.Empty,
        AutoStart: false,
        AutoPreComplete: false,
        AutoAccept: false,
        AutoComplete: false,
        Repeatable: false,
        ScriptedStart: false,
        ScriptedEnd: false,
        Blocked: false,
        ViewMedalItem: 0,
        SelectedSkillId: 0,
        StartRequirements: Array.Empty<QuestRequirement>(),
        CompleteRequirements: Array.Empty<QuestRequirement>(),
        StartActions: Array.Empty<QuestAction>(),
        CompleteActions: Array.Empty<QuestAction>(),
        RelevantMobs: new Dictionary<int, int>());
}

public interface IQuestCatalog
{
    QuestDefinition? GetQuest(int questId);
}
