namespace Maple.Core.Characters;

/// <summary>角色好友清單。文件持久化於 <see cref="Character"/>，執行期由 Application 協調跨玩家通知。</summary>
public sealed class BuddyList
{
    public const string DefaultGroup = "尚未設定群組";

    public byte Capacity { get; set; } = 20;

    public List<BuddyEntry> Entries { get; set; } = new();

    public bool Contains(int characterId) => Get(characterId) is not null;

    public bool ContainsVisible(int characterId) => Get(characterId)?.Visible == true;

    public bool IsFull() => Entries.Count >= Capacity;

    public IReadOnlyList<int> GetBuddyIds() => Entries.Select(static e => e.CharacterId).ToList();

    public BuddyEntry? Get(int characterId)
        => Entries.FirstOrDefault(e => e.CharacterId == characterId);

    public BuddyEntry? Get(string characterName)
        => Entries.FirstOrDefault(e => string.Equals(e.Name, characterName, StringComparison.OrdinalIgnoreCase));

    public void Put(BuddyEntry entry)
    {
        var existing = Entries.FindIndex(e => e.CharacterId == entry.CharacterId);
        if (existing >= 0)
        {
            Entries[existing] = entry;
            return;
        }

        Entries.Add(entry);
    }

    public bool Remove(int characterId)
    {
        var existing = Entries.FindIndex(e => e.CharacterId == characterId);
        if (existing < 0)
        {
            return false;
        }

        Entries.RemoveAt(existing);
        return true;
    }

    public BuddyEntry? TakeNextPendingRequest()
    {
        var entry = Entries.FirstOrDefault(static e => e.PendingRequest && !e.Visible && !e.RequestPrompted);
        if (entry is not null)
        {
            entry.RequestPrompted = true;
        }

        return entry;
    }

    public void ResetRuntimeState()
    {
        foreach (var entry in Entries)
        {
            entry.Channel = -1;
            entry.RequestPrompted = false;
        }
    }
}
