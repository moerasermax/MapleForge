using Maple.Core.Inventory;
using Maple.Core.Quests;
using Maple.Core.World;

namespace Maple.Application.Quests;

public enum QuestClientActionKind : byte
{
    RestoreLostItem = 0,
    Start = 1,
    Complete = 2,
    Forfeit = 3,
    ScriptedStart = 4,
    ScriptedComplete = 5,
}

public enum QuestTransactionStatus
{
    Success,
    CannotStart,
    CannotComplete,
    CannotForfeit,
    NotEnoughItems,
    NotEnoughMeso,
    InventoryFull,
    ScriptRequired,
    UnsupportedAction,
}

public sealed record QuestClientAction(
    QuestClientActionKind Kind,
    int QuestId,
    int NpcId = 0,
    int? Selection = null,
    int? RestoreItemId = null);

public sealed record QuestTransactionResult
{
    public QuestTransactionResult(
        QuestTransactionStatus status,
        QuestRecord? quest = null,
        IReadOnlyList<Item>? gainedItems = null,
        IReadOnlyList<QuestInventoryMutation>? inventoryMutations = null,
        bool mesoChanged = false,
        int meso = 0,
        int? showQuestCompletionId = null,
        int? nextQuestId = null)
    {
        Status = status;
        Quest = quest;
        GainedItems = gainedItems ?? Array.Empty<Item>();
        InventoryMutations = inventoryMutations ?? Array.Empty<QuestInventoryMutation>();
        MesoChanged = mesoChanged;
        Meso = meso;
        ShowQuestCompletionId = showQuestCompletionId;
        NextQuestId = nextQuestId;
    }

    public QuestTransactionStatus Status { get; init; }

    public QuestRecord? Quest { get; init; }

    public IReadOnlyList<Item> GainedItems { get; init; }

    public IReadOnlyList<QuestInventoryMutation> InventoryMutations { get; init; }

    public bool MesoChanged { get; init; }

    public int Meso { get; init; }

    public int? ShowQuestCompletionId { get; init; }

    public int? NextQuestId { get; init; }

    /// <summary>P163：exp／pop／sp 造成的屬性更新（升級時含整串升級更新）。</summary>
    public IReadOnlyList<PlayerStatUpdate> StatUpdates { get; init; } = [];

    /// <summary>P163：Java gainExp(total, show, inChat, white=true) 的 total（呼叫端送 GainEXP_Others）。</summary>
    public int ExpGained { get; init; }

    /// <summary>P163：Java getShowFameGain(gain)。</summary>
    public int FameGained { get; init; }

    /// <summary>P163：changeSkillLevel 結果（技能、等級、master）。</summary>
    public IReadOnlyList<(int SkillId, byte Level, byte MasterLevel)> SkillChanges { get; init; } = [];

    /// <summary>P163：buffItemID → getItemEffect(id).applyTo(c)（呼叫端套用道具效果）。</summary>
    public int? BuffItemId { get; init; }

    /// <summary>P166：cm.gainMeso → gainMeso(gain, show, inChat) 的 showMesoGain(gain, true)。</summary>
    public int? MesoGained { get; init; }

    /// <summary>P166：cm.gainItem／removeAll → getShowItemGain(id, quantity, true)（負數為扣除）。</summary>
    public IReadOnlyList<(int ItemId, int Quantity)> ItemGainNotices { get; init; } = [];
}

/// <summary>Quest use case; protocol encoding stays in the adapter layer.</summary>
public sealed class QuestService
{
    private readonly IQuestCatalog _catalog;

    private readonly Maple.Application.Combat.PlayerLocalStatsCalculator? _localStats;

    public QuestService(IQuestCatalog catalog, Maple.Application.Combat.PlayerLocalStatsCalculator? localStats = null)
    {
        _localStats = localStats;
        _catalog = catalog;
    }

    /// <summary>
    /// P166：NPC 腳本 <c>cm.gainExp(gain)</c> → <c>gainExp(gain, show, inChat, white)</c>（升級加成同任務經驗）；
    /// 以 <see cref="QuestTransactionResult"/> 承載屬性更新與經驗提示（呼叫端共用同一套送包）。
    /// </summary>
    public QuestTransactionResult GrantExp(Player player, int amount)
    {
        ArgumentNullException.ThrowIfNull(player);

        var bonus = _localStats is not null && player.WouldLevelUp(amount) ? _localStats.ComputeLevelUpBonus(player) : default;
        var mutation = player.GainExperience(amount, bonus: bonus);
        return new QuestTransactionResult(QuestTransactionStatus.Success)
        {
            StatUpdates = mutation.Updates,
            ExpGained = mutation.Updates.Count > 0 ? amount : 0,
        };
    }

    public QuestTransactionResult HandleClientAction(Player player, QuestClientAction action)
        => action.Kind switch
        {
            QuestClientActionKind.RestoreLostItem => RestoreLostItem(player, action.QuestId, action.RestoreItemId ?? 0),
            QuestClientActionKind.Start => StartQuest(player, action.QuestId, action.NpcId),
            QuestClientActionKind.Complete => CompleteQuest(player, action.QuestId, action.NpcId, action.Selection),
            QuestClientActionKind.Forfeit => ForfeitQuest(player, action.QuestId),
            QuestClientActionKind.ScriptedStart or QuestClientActionKind.ScriptedComplete =>
                new QuestTransactionResult(QuestTransactionStatus.ScriptRequired),
            _ => new QuestTransactionResult(QuestTransactionStatus.UnsupportedAction),
        };

    public QuestTransactionResult StartQuest(Player player, int questId, int npc)
    {
        var quest = GetQuest(questId);
        if (quest.ScriptedStart && !IsForceStartFromClient(questId, npc))
        {
            return new QuestTransactionResult(QuestTransactionStatus.ScriptRequired);
        }

        if (IsForceStartFromClient(questId, npc))
        {
            var forced = player.ForceStartQuest(questId, npc, null, quest.RelevantMobs);
            return new QuestTransactionResult(QuestTransactionStatus.Success, forced);
        }

        if (!CanStart(player, quest, npc))
        {
            return new QuestTransactionResult(QuestTransactionStatus.CannotStart);
        }

        var precheck = CheckActions(player, quest.StartActions, selection: null);
        if (precheck != QuestTransactionStatus.Success)
        {
            return new QuestTransactionResult(precheck);
        }

        // P163：Java runStart 的 exp／money／pop 在 forfeited > 0 時略過。
        var forfeited = player.GetQuest(questId).Forfeited > 0;
        var actionResult = ApplyActions(player, quest.StartActions, selection: null, skipRewards: forfeited);
        if (actionResult.Status != QuestTransactionStatus.Success)
        {
            return actionResult;
        }

        var record = player.ForceStartQuest(questId, npc, null, quest.RelevantMobs);
        return actionResult with { Quest = record };
    }

    public QuestTransactionResult CompleteQuest(Player player, int questId, int npc, int? selection = null)
    {
        var quest = GetQuest(questId);
        if (!CanComplete(player, quest, npc))
        {
            return new QuestTransactionResult(QuestTransactionStatus.CannotComplete);
        }

        var precheck = CheckActions(player, quest.CompleteActions, selection);
        if (precheck != QuestTransactionStatus.Success)
        {
            return new QuestTransactionResult(precheck);
        }

        var record = player.ForceCompleteQuest(questId, npc);
        var actionResult = ApplyActions(player, quest.CompleteActions, selection);
        if (actionResult.Status != QuestTransactionStatus.Success)
        {
            return actionResult;
        }

        return actionResult with
        {
            Quest = record,
            ShowQuestCompletionId = questId,
        };
    }

    public QuestTransactionResult ForfeitQuest(Player player, int questId)
    {
        var record = player.ForfeitQuest(questId);
        return record is null
            ? new QuestTransactionResult(QuestTransactionStatus.CannotForfeit)
            : new QuestTransactionResult(QuestTransactionStatus.Success, record);
    }

    public QuestTransactionResult ForceStartQuest(Player player, int questId, int npc, string? customData = null)
    {
        var quest = GetQuest(questId);
        var record = player.ForceStartQuest(questId, npc, customData, quest.RelevantMobs);
        return new QuestTransactionResult(QuestTransactionStatus.Success, record);
    }

    public QuestTransactionResult ForceCompleteQuest(Player player, int questId, int npc)
    {
        var record = player.ForceCompleteQuest(questId, npc);
        return new QuestTransactionResult(QuestTransactionStatus.Success, record, showQuestCompletionId: questId);
    }

    public QuestTransactionResult UpdateQuest(Player player, int questId)
    {
        var record = player.GetQuest(questId);
        return new QuestTransactionResult(QuestTransactionStatus.Success, record);
    }

    private QuestTransactionResult RestoreLostItem(Player player, int questId, int itemId)
    {
        if (itemId <= 0)
        {
            return new QuestTransactionResult(QuestTransactionStatus.UnsupportedAction);
        }

        var quest = GetQuest(questId);
        var itemAction = quest.StartActions
            .Where(a => a.Kind == QuestActionKind.Item)
            .SelectMany(a => a.Items ?? Array.Empty<QuestItemAction>())
            .FirstOrDefault(i => i.ItemId == itemId && i.Count > 0 && CanGetItem(i, player));

        if (itemAction is null)
        {
            return new QuestTransactionResult(QuestTransactionStatus.UnsupportedAction);
        }

        if (player.Inventory.By(Player.InventoryTypeOf(itemId)).CountById(itemId) >= itemAction.Count)
        {
            return new QuestTransactionResult(QuestTransactionStatus.Success);
        }

        var gained = player.GainItem(Player.InventoryTypeOf(itemId), itemId, (short)Math.Clamp(itemAction.Count, 1, short.MaxValue));
        if (gained is null)
        {
            return new QuestTransactionResult(QuestTransactionStatus.InventoryFull);
        }

        player.FlushInventory();
        return new QuestTransactionResult(QuestTransactionStatus.Success, gainedItems: new[] { gained });
    }

    private QuestDefinition GetQuest(int questId) => _catalog.GetQuest(questId) ?? QuestDefinition.Empty(questId);

    private static bool CanStart(Player player, QuestDefinition quest, int npc)
    {
        var current = player.GetQuest(quest.Id);
        if (current.Status != (byte)QuestStatus.NotStarted &&
            !(current.Status == (byte)QuestStatus.Completed && quest.Repeatable))
        {
            return false;
        }

        return quest.StartRequirements.All(req => CheckRequirement(player, quest, req, npc));
    }

    private static bool CanComplete(Player player, QuestDefinition quest, int npc)
    {
        if (player.GetQuest(quest.Id).Status != (byte)QuestStatus.Started)
        {
            return false;
        }

        return quest.CompleteRequirements.All(req => CheckRequirement(player, quest, req, npc));
    }

    private static bool CheckRequirement(Player player, QuestDefinition quest, QuestRequirement req, int? npc)
    {
        var values = req.Values ?? Array.Empty<QuestIntPair>();
        if (values.Count == 0 && req.Kind is QuestRequirementKind.Job or QuestRequirementKind.Item or QuestRequirementKind.Quest or QuestRequirementKind.Mob)
        {
            return true;
        }

        return req.Kind switch
        {
            QuestRequirementKind.Job => values.Any(v => v.Value == player.Character.Job),
            QuestRequirementKind.Quest => values.All(v =>
                v.Value == 0 || player.GetQuestStatus(v.Id) == (byte)v.Value),
            QuestRequirementKind.Item => values.All(v =>
            {
                var count = player.Inventory.By(Player.InventoryTypeOf(v.Id)).CountById(v.Id);
                return v.Value > 0 ? count >= v.Value : count <= 0;
            }),
            QuestRequirementKind.LevelMin => player.Character.Level >= req.IntValue,
            QuestRequirementKind.LevelMax => player.Character.Level <= req.IntValue,
            QuestRequirementKind.Mob => values.All(v => player.GetMobKillCount(quest.Id, v.Id) >= v.Value),
            QuestRequirementKind.Npc => npc is null || npc.Value == req.IntValue,
            QuestRequirementKind.FieldEnter => req.IntValue == player.Character.MapId,
            QuestRequirementKind.Interval => CheckInterval(player.GetQuest(quest.Id), req.IntValue),
            QuestRequirementKind.QuestComplete => player.Character.Quests.Count(q => q.Status == (byte)QuestStatus.Completed) >= req.IntValue,
            QuestRequirementKind.Pop => player.Character.Fame <= req.IntValue,
            _ => true,
        };
    }

    private static QuestTransactionStatus CheckActions(Player player, IReadOnlyList<QuestAction> actions, int? selection)
    {
        var requiredFreeSlots = new Dictionary<InventoryType, int>();
        foreach (var action in actions)
        {
            switch (action.Kind)
            {
                case QuestActionKind.Item:
                    foreach (var item in ResolveItems(action, player, selection))
                    {
                        if (item.Count < 0)
                        {
                            var have = player.Inventory.By(Player.InventoryTypeOf(item.ItemId)).CountById(item.ItemId);
                            if (have < Math.Abs(item.Count))
                            {
                                return QuestTransactionStatus.NotEnoughItems;
                            }
                        }
                        else if (item.Count > 0)
                        {
                            var type = Player.InventoryTypeOf(item.ItemId);
                            requiredFreeSlots[type] = requiredFreeSlots.GetValueOrDefault(type) + 1;
                        }
                    }
                    break;

                case QuestActionKind.Money:
                    var next = (long)player.Character.Meso + action.IntValue;
                    if (next < 0 || next > int.MaxValue)
                    {
                        return QuestTransactionStatus.NotEnoughMeso;
                    }
                    break;
            }
        }

        foreach (var (type, need) in requiredFreeSlots)
        {
            var bag = player.Inventory.By(type);
            var free = bag.SlotLimit - bag.Items.Count;
            if (free < need)
            {
                return QuestTransactionStatus.InventoryFull;
            }
        }

        return QuestTransactionStatus.Success;
    }

    private QuestTransactionResult ApplyActions(Player player, IReadOnlyList<QuestAction> actions, int? selection, bool skipRewards = false)
    {
        var statUpdates = new List<PlayerStatUpdate>();
        var skillChanges = new List<(int SkillId, byte Level, byte MasterLevel)>();
        var expGained = 0;
        var fameGained = 0;
        int? buffItemId = null;
        var gained = new List<Item>();
        var removed = new List<QuestInventoryMutation>();
        var mesoChanged = false;
        int? nextQuestId = null;

        foreach (var action in actions)
        {
            switch (action.Kind)
            {
                case QuestActionKind.Item:
                    foreach (var item in ResolveItems(action, player, selection))
                    {
                        if (item.Count < 0)
                        {
                            if (!player.TryTakeItemById(
                                    Player.InventoryTypeOf(item.ItemId),
                                    item.ItemId,
                                    (short)Math.Clamp(Math.Abs(item.Count), 1, short.MaxValue),
                                    out var mutations))
                            {
                                return new QuestTransactionResult(QuestTransactionStatus.NotEnoughItems);
                            }

                            removed.AddRange(mutations);
                        }
                        else if (item.Count > 0)
                        {
                            var added = player.GainItem(
                                Player.InventoryTypeOf(item.ItemId),
                                item.ItemId,
                                (short)Math.Clamp(item.Count, 1, short.MaxValue));
                            if (added is null)
                            {
                                return new QuestTransactionResult(QuestTransactionStatus.InventoryFull);
                            }

                            gained.Add(added);
                        }
                    }
                    break;

                case QuestActionKind.Money:
                    if (skipRewards)
                    {
                        break;
                    }

                    player.GainMeso(action.IntValue);
                    mesoChanged = true;
                    break;

                case QuestActionKind.Exp when !skipRewards && action.IntValue != 0:
                    // P163：Java gainExp(intStore * getExpRate_Quest(level)=1, show, inChat, white)。
                    var bonus = _localStats is not null && player.WouldLevelUp(action.IntValue) ? _localStats.ComputeLevelUpBonus(player) : default;
                    statUpdates.AddRange(player.GainExperience(action.IntValue, bonus: bonus).Updates);
                    expGained += action.IntValue;
                    break;

                case QuestActionKind.Pop when !skipRewards:
                    // P163：Java addFame（無上下限）→ updateSingleStat(FAME) → getShowFameGain。
                    player.Character.Fame = (short)(player.Character.Fame + action.IntValue);
                    statUpdates.Add(new PlayerStatUpdate(PlayerStatKind.Fame, player.Character.Fame));
                    fameGained += action.IntValue;
                    break;

                case QuestActionKind.Skill:
                    // P163：初心者技能（id/10000 ∈ {0,1000,2000,2001,3000}）或職業在 job 清單 → 等級／master 取較大值。
                    foreach (var skill in action.Skills ?? [])
                    {
                        if (!IsBeginnerSkill(skill.SkillId) && !skill.Jobs.Contains(player.Character.Job))
                        {
                            continue;
                        }

                        var level = (byte)Math.Max(skill.SkillLevel, player.GetSkillLevel(skill.SkillId));
                        var master = (byte)Math.Max(skill.MasterLevel, player.GetMasterLevel(skill.SkillId));
                        player.ChangeSkillLevel(skill.SkillId, level, master);
                        skillChanges.Add((skill.SkillId, level, master));
                    }

                    break;

                case QuestActionKind.Sp when action.IntValue != 0:
                    // P163：Java gainSP(sp_val)（v113 非龍魔，skillbook 恆 0）。
                    player.Character.RemainingSp = (short)(player.Character.RemainingSp + action.IntValue);
                    statUpdates.Add(new PlayerStatUpdate(PlayerStatKind.AvailableSp, player.Character.RemainingSp));
                    break;

                case QuestActionKind.BuffItemId when action.IntValue != -1 && action.IntValue != 0:
                    buffItemId = action.IntValue;
                    break;

                case QuestActionKind.Quest:
                    foreach (var state in action.QuestStates ?? Array.Empty<QuestIntPair>())
                    {
                        player.SetQuestStatus(state.Id, (QuestStatus)Math.Clamp(state.Value, 0, 2));
                    }
                    break;

                case QuestActionKind.NextQuest:
                    if (action.IntValue > 0)
                    {
                        nextQuestId = action.IntValue;
                    }
                    break;
            }
        }

        if (gained.Count > 0 || removed.Count > 0)
        {
            player.FlushInventory();
        }

        return new QuestTransactionResult(
            QuestTransactionStatus.Success,
            gainedItems: gained,
            inventoryMutations: removed,
            mesoChanged: mesoChanged,
            meso: player.Character.Meso,
            nextQuestId: nextQuestId)
        {
            StatUpdates = statUpdates,
            ExpGained = expGained,
            FameGained = fameGained,
            SkillChanges = skillChanges,
            BuffItemId = buffItemId,
        };
    }

    /// <summary>Java <c>Skill.isBeginnerSkill</c>。</summary>
    private static bool IsBeginnerSkill(int skillId) => skillId / 10000 is 0 or 1000 or 2000 or 2001 or 3000;

    private static IReadOnlyList<QuestItemAction> ResolveItems(QuestAction action, Player player, int? selection)
    {
        var items = (action.Items ?? Array.Empty<QuestItemAction>())
            .Where(i => CanGetItem(i, player))
            .ToArray();
        if (items.Length == 0)
        {
            return Array.Empty<QuestItemAction>();
        }

        var randomPool = new List<int>();
        foreach (var item in items.Where(i => i.Prop > 0))
        {
            for (var i = 0; i < item.Prop; i++)
            {
                randomPool.Add(item.ItemId);
            }
        }

        var randomSelection = randomPool.Count == 0 ? 0 : randomPool[Random.Shared.Next(randomPool.Count)];
        var selected = new List<QuestItemAction>();
        var selectableIndex = 0;

        foreach (var item in items)
        {
            if (item.Prop == -2 || (item.Prop == 0 && randomPool.Count == 0))
            {
                selected.Add(item);
            }
            else if (item.Prop == -1)
            {
                if (selection is not null && selection.Value == selectableIndex)
                {
                    selected.Add(item);
                }
                selectableIndex++;
            }
            else if (item.Prop > 0 && item.ItemId == randomSelection)
            {
                selected.Add(item);
            }
        }

        return selected;
    }

    private static bool CanGetItem(QuestItemAction item, Player player)
    {
        if (item.Gender != 2 && item.Gender >= 0 && item.Gender != player.Character.Gender)
        {
            return false;
        }

        if (item.Job <= 0)
        {
            return true;
        }

        var playerJob = player.Character.Job;
        var found = GetJobBy5ByteEncoding(item.Job).Any(job => job / 100 == playerJob / 100);
        if (!found && item.JobEx > 0)
        {
            found = GetJobBySimpleEncoding(item.JobEx).Any(job => job / 100 % 10 == playerJob / 100 % 10);
        }

        return found;
    }

    private static bool CheckInterval(QuestRecord record, int minutes)
    {
        if (record.Status != (byte)QuestStatus.Completed)
        {
            return true;
        }

        var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - record.CompletionTimeUnixMillis;
        return elapsed >= (long)minutes * 60_000L;
    }

    private static bool IsForceStartFromClient(int questId, int npc)
        => (npc == 0 && questId > 0) || questId is 2001 or 8511 or 21301 or 21302 or 3083;

    private static IEnumerable<int> GetJobBy5ByteEncoding(int encoded)
    {
        if ((encoded & 0x1) != 0) yield return 0;
        if ((encoded & 0x2) != 0) yield return 100;
        if ((encoded & 0x4) != 0) yield return 200;
        if ((encoded & 0x8) != 0) yield return 300;
        if ((encoded & 0x10) != 0) yield return 400;
        if ((encoded & 0x20) != 0) yield return 500;
        if ((encoded & 0x400) != 0) yield return 1000;
        if ((encoded & 0x800) != 0) yield return 1100;
        if ((encoded & 0x1000) != 0) yield return 1200;
        if ((encoded & 0x2000) != 0) yield return 1300;
        if ((encoded & 0x4000) != 0) yield return 1400;
        if ((encoded & 0x8000) != 0) yield return 1500;
        if ((encoded & 0x20000) != 0) { yield return 2001; yield return 2200; }
        if ((encoded & 0x100000) != 0) { yield return 2000; yield return 2001; }
        if ((encoded & 0x200000) != 0) yield return 2100;
        if ((encoded & 0x400000) != 0) { yield return 2001; yield return 2200; }
        if ((encoded & 0x40000000) != 0) { yield return 3000; yield return 3200; yield return 3300; yield return 3500; }
    }

    private static IEnumerable<int> GetJobBySimpleEncoding(int encoded)
    {
        if ((encoded & 0x1) != 0) yield return 200;
        if ((encoded & 0x2) != 0) yield return 300;
        if ((encoded & 0x4) != 0) yield return 400;
        if ((encoded & 0x8) != 0) yield return 500;
    }
}
