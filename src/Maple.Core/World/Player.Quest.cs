using Maple.Core.Inventory;
using Maple.Core.Quests;

namespace Maple.Core.World;

public sealed partial class Player
{
    public QuestRecord GetQuest(int questId)
        => FindQuest(questId) ?? NewQuestRecord(questId);

    public QuestRecord GetOrAddQuest(int questId)
    {
        var existing = FindQuest(questId);
        if (existing is not null)
        {
            return existing;
        }

        var record = NewQuestRecord(questId);
        Character.Quests.Add(record);
        return record;
    }

    public byte GetQuestStatus(int questId) => GetQuest(questId).Status;

    public QuestRecord ForceStartQuest(int questId, int npc, string? customData, IReadOnlyDictionary<int, int>? relevantMobs)
    {
        var old = GetQuest(questId);
        var oldForfeited = old.Forfeited;
        var oldCompletion = old.CompletionTimeUnixMillis;

        var record = GetOrAddQuest(questId);
        record.Status = (byte)QuestStatus.Started;
        record.Npc = npc;
        record.Forfeited = oldForfeited;
        record.CompletionTimeUnixMillis = oldCompletion > 0 ? oldCompletion : NowUnixMillis();
        record.CustomData = customData;
        record.MobKills = relevantMobs is null
            ? new List<QuestMobKillRecord>()
            : relevantMobs.Select(kv => new QuestMobKillRecord { MobId = kv.Key, Count = 0 }).ToList();
        return record;
    }

    public QuestRecord ForceCompleteQuest(int questId, int npc)
    {
        var old = GetQuest(questId);
        var oldForfeited = old.Forfeited;

        var record = GetOrAddQuest(questId);
        record.Status = (byte)QuestStatus.Completed;
        record.Npc = npc;
        record.Forfeited = oldForfeited;
        record.CompletionTimeUnixMillis = NowUnixMillis();
        record.CustomData = null;
        record.MobKills.Clear();
        return record;
    }

    public QuestRecord? ForfeitQuest(int questId)
    {
        var old = FindQuest(questId);
        if (old is null || old.Status != (byte)QuestStatus.Started)
        {
            return null;
        }

        old.Status = (byte)QuestStatus.NotStarted;
        old.Npc = 0;
        old.Forfeited++;
        old.CustomData = null;
        old.MobKills.Clear();
        return old;
    }

    public QuestRecord SetQuestStatus(int questId, QuestStatus status)
    {
        var record = GetOrAddQuest(questId);
        record.Status = (byte)status;
        record.Npc = 0;
        record.CompletionTimeUnixMillis = NowUnixMillis();
        record.CustomData = null;
        record.MobKills.Clear();
        return record;
    }

    public int GetMobKillCount(int questId, int mobId)
    {
        var record = FindQuest(questId);
        return record?.MobKills.FirstOrDefault(k => k.MobId == mobId)?.Count ?? 0;
    }

    public bool TrySetMobKillCount(int questId, int mobId, int count)
    {
        var record = FindQuest(questId);
        if (record is null || record.Status != (byte)QuestStatus.Started)
        {
            return false;
        }

        var kill = record.MobKills.FirstOrDefault(k => k.MobId == mobId);
        if (kill is null)
        {
            return false;
        }

        kill.Count = Math.Max(0, count);
        return true;
    }

    public string GetInfoQuest(int questId)
        => Character.QuestInfo.FirstOrDefault(q => q.QuestId == questId)?.Data ?? string.Empty;

    public void UpdateInfoQuest(int questId, string? data)
    {
        var record = Character.QuestInfo.FirstOrDefault(q => q.QuestId == questId);
        if (record is null)
        {
            Character.QuestInfo.Add(new QuestInfoRecord { QuestId = questId, Data = data ?? string.Empty });
            return;
        }

        record.Data = data ?? string.Empty;
    }

    public void ClearInfoQuest(int questId)
    {
        Character.QuestInfo.RemoveAll(q => q.QuestId == questId);
    }

    public bool TryTakeItemById(
        InventoryType type,
        int itemId,
        short quantity,
        out IReadOnlyList<QuestInventoryMutation> mutations)
    {
        mutations = Array.Empty<QuestInventoryMutation>();
        if (quantity <= 0)
        {
            return false;
        }

        var bag = Inventory.By(type);
        if (bag.CountById(itemId) < quantity)
        {
            return false;
        }

        var remaining = quantity;
        var changed = new List<QuestInventoryMutation>();
        foreach (var item in bag.Items.Where(i => i.ItemId == itemId).OrderBy(i => i.Slot).ToArray())
        {
            if (remaining <= 0)
            {
                break;
            }

            var oldQuantity = item.IsEquip ? (short)1 : item.Quantity;
            var take = (short)Math.Min(remaining, oldQuantity);
            if (!bag.TryTake(item.Slot, take, out _))
            {
                return false;
            }

            remaining -= take;
            changed.Add(new QuestInventoryMutation(type, item.Slot, itemId, oldQuantity, (short)(oldQuantity - take)));
        }

        mutations = changed;
        return remaining == 0;
    }

    private QuestRecord? FindQuest(int questId)
        => Character.Quests.FirstOrDefault(q => q.QuestId == questId);

    private static QuestRecord NewQuestRecord(int questId) => new()
    {
        QuestId = questId,
        Status = (byte)QuestStatus.NotStarted,
        CompletionTimeUnixMillis = NowUnixMillis(),
    };

    private static long NowUnixMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
