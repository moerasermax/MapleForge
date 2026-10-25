using Maple.Core.Squads;

namespace Maple.Application.Squads;

/// <summary>P189：遠征隊要通知的對象與內容（呼叫端轉成封包）。</summary>
public abstract record SquadNotice;

/// <summary>對開始地圖廣播 getClock(seconds)。</summary>
public sealed record SquadMapClock(int MapId, int Seconds) : SquadNotice;

/// <summary>對開始地圖廣播 stopClock。</summary>
public sealed record SquadMapStopClock(int MapId) : SquadNotice;

/// <summary>對開始地圖廣播 getItemNotice(msg)（serverNotice 6）。</summary>
public sealed record SquadMapNotice(int MapId, string Message) : SquadNotice;

/// <summary>對某角色 dropMessage(type, msg)。</summary>
public sealed record SquadPlayerNotice(string CharacterName, int Type, string Message) : SquadNotice;

/// <summary>P189：遠征隊相關的線上角色資訊（Java getChar(name)）。</summary>
public sealed record SquadCharacter(int Id, string Name, int Job, int MapId, int ChannelIndex);

/// <summary>
/// P189：頻道的遠征隊表（Java <c>ChannelServer.mapleSquads</c>＋<c>MapleSquadType.queue</c>）。Java 的排隊表是 enum 上的 static
/// 狀態，這裡改為每個登記表實例各自持有（多實例安全）。所有方法自帶鎖。
/// </summary>
public sealed class SquadRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MapleSquad> _squads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Name, DateTimeOffset Time)>> _queues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Name, string Result)>> _queuedResults = new(StringComparer.Ordinal);
    private readonly Func<string, SquadCharacter?> _findOnline;
    private readonly TimeProvider _time;

    public SquadRegistry(Func<string, SquadCharacter?>? findOnline = null, TimeProvider? time = null)
    {
        _findOnline = findOnline ?? (static _ => null);
        _time = time ?? TimeProvider.System;
    }

    public DateTimeOffset Now => _time.GetUtcNow();

    public SquadCharacter? FindOnline(string name) => _findOnline(name);

    /// <summary>Java getMapleSquad(type)：不合法的類型 Java 會拋例外（valueOf），這裡回 null。</summary>
    public MapleSquad? Get(string type)
    {
        lock (_gate)
        {
            return _squads.GetValueOrDefault(MapleSquad.NormalizeType(type));
        }
    }

    /// <summary>Java NPCConversationManager.registerSquad：類型合法且尚無遠征隊時建立，廣播 clock(minutes×60) 與「隊長名＋startText」。</summary>
    public bool Register(string type, SquadCharacter leader, int minutes, string startText, out IReadOnlyList<SquadNotice> notices)
    {
        notices = [];
        var key = MapleSquad.NormalizeType(type);
        if (!MapleSquad.IsValidType(key))
        {
            return false;
        }

        lock (_gate)
        {
            if (_squads.ContainsKey(key))
            {
                return false;
            }

            _squads[key] = new MapleSquad(key, leader.Id, leader.Name, leader.Job, leader.MapId, Now, minutes * 60 * 1000, startText);
            _queues.TryAdd(key, []);
            _queuedResults.TryAdd(key, []);
        }

        notices = [new SquadMapClock(leader.MapId, minutes * 60), new SquadMapNotice(leader.MapId, leader.Name + startText)];
        return true;
    }

    /// <summary>Java getLeader()：隊長不在線時清除遠征隊並回 null。</summary>
    public SquadCharacter? GetLeader(MapleSquad squad, List<SquadNotice> notices)
    {
        if (squad.Members.Count > 0 && _findOnline(squad.LeaderName) is { } leader)
        {
            return leader;
        }

        if (squad.Status != 0)
        {
            Clear(squad, notices);
        }

        return null;
    }

    /// <summary>Java clear()：取消到期排程（開始地圖 stopClock）、清名單、移出頻道表、狀態 0。</summary>
    public void Clear(MapleSquad squad, List<SquadNotice> notices)
    {
        lock (_gate)
        {
            if (squad.Status == 1)
            {
                notices.Add(new SquadMapStopClock(squad.BeginMapId));
            }

            if (_squads.TryGetValue(squad.Type, out var current) && ReferenceEquals(current, squad))
            {
                _squads.Remove(squad.Type);
            }

            squad.Clear();
        }
    }

    /// <summary>Java addMember 包裝：隊長不在線 −1；成功時通知隊長「XX (職業) 加入／離開了遠征隊」。</summary>
    public int AddMember(string type, SquadCharacter member, bool join, List<SquadNotice> notices)
    {
        if (Get(type) is not { } squad)
        {
            return -1;
        }

        if (GetLeader(squad, notices) is not { } leader)
        {
            return -1;
        }

        int result;
        lock (_gate)
        {
            result = squad.AddMember(member.Name, member.Job, join, IsQueued(squad.Type, member.Name));
        }

        if (result == 1)
        {
            var job = JobNames.Of(member.Job);
            notices.Add(new SquadPlayerNotice(leader.Name, 6, join ? $"{member.Name} ({job}) 加入了遠征隊!" : $"{member.Name} ({job}) 離開了遠征隊."));
        }

        return result;
    }

    public void BanMember(string type, int pos, List<SquadNotice> notices)
    {
        if (Get(type) is not { } squad)
        {
            return;
        }

        string? banned;
        lock (_gate)
        {
            banned = squad.BanMember(pos, n => _findOnline(n) is not null);
        }

        if (banned is not null)
        {
            notices.Add(new SquadPlayerNotice(banned, 5, squad.LeaderName + " 從遠征隊中刪除您."));
        }
    }

    public void AcceptMember(string type, int pos, List<SquadNotice> notices)
    {
        if (Get(type) is not { } squad)
        {
            return;
        }

        string? accepted;
        lock (_gate)
        {
            accepted = squad.AcceptMember(pos, n => _findOnline(n) is not null);
        }

        if (accepted is not null)
        {
            notices.Add(new SquadPlayerNotice(accepted, 5, squad.LeaderName + " 允許你重新回來遠征隊"));
        }
    }

    // ── 排隊（Java getNextPlayer／setNextPlayer／getAllNextPlayer）──────────────────────────
    public IReadOnlyList<(string Name, DateTimeOffset Time)> GetQueue(string type)
    {
        lock (_gate)
        {
            return _queues.TryGetValue(MapleSquad.NormalizeType(type), out var queue) ? queue.ToArray() : [];
        }
    }

    /// <summary>Java setNextPlayer：已在本類型排隊則移除；已在任何類型排隊則不動；否則加入。</summary>
    public void SetNextPlayer(string type, string name)
    {
        var key = MapleSquad.NormalizeType(type);
        lock (_gate)
        {
            var queue = Queue(key);
            var index = queue.FindIndex(q => q.Name == name);
            if (index >= 0)
            {
                queue.RemoveAt(index);
                return;
            }

            if (_queues.Values.Any(q => q.Any(e => e.Name == name)))
            {
                return;
            }

            queue.Add((name, Now));
        }
    }

    public string NextPlayerString(string type)
    {
        var queue = GetQueue(type);
        var sb = new System.Text.StringBuilder("\n排隊成員 : ");
        sb.Append("#b").Append(queue.Count).Append(" #k ").Append("與遠征隊名單 : \n\r ");
        var i = 0;
        foreach (var (name, _) in queue)
        {
            i++;
            sb.Append(i).Append(" : ").Append(name).Append(" \n\r ");
        }

        sb.Append("你是否想要 #e當下一個#n 在遠征隊排隊中　或者 #e移除#n 在遠征隊? 如果你想的話...");
        return sb.ToString();
    }

    /// <summary>
    /// Java scheduleRemoval：到期時（狀態仍 1，或隊長已不在線）clear 並 copy()——從排隊中依時間取最早者，
    /// 在線、在開始地圖且同頻道則接手為新隊長（廣播 clock 與 toSay），否則記錄原因並繼續下一位。
    /// </summary>
    public IReadOnlyList<SquadNotice> Tick(int channelIndex)
    {
        var notices = new List<SquadNotice>();
        MapleSquad[] expired;
        lock (_gate)
        {
            var now = Now;
            // setStatus(2)（開戰）會取消到期排程，所以只有招募中（1）的會到期。
            expired = _squads.Values.Where(s => s.Status == 1 && s.TimeLeftMs(now) <= 0).ToArray();
        }

        foreach (var squad in expired)
        {
            Clear(squad, notices);
            Copy(squad, channelIndex, notices);
        }

        return notices;
    }

    private void Copy(MapleSquad old, int channelIndex, List<SquadNotice> notices)
    {
        while (true)
        {
            (string Name, DateTimeOffset Time) next;
            lock (_gate)
            {
                var queue = Queue(old.Type);
                if (queue.Count == 0 || _squads.ContainsKey(old.Type))
                {
                    return;
                }

                var index = 0;
                for (var i = 1; i < queue.Count; i++)
                {
                    if (queue[i].Time < queue[index].Time)
                    {
                        index = i;
                    }
                }

                next = queue[index];
                queue.RemoveAt(index);
            }

            var results = Results(old.Type);
            if (_findOnline(next.Name) is not { } lead)
            {
                notices.Add(new SquadMapNotice(old.BeginMapId, next.Name + "'遠征隊已經結束了，由於有成員沒有在線上"));
                lock (_gate) { results.Add((next.Name, "沒有上線")); }
                continue;
            }

            if (lead.MapId != old.BeginMapId || lead.ChannelIndex != channelIndex)
            {
                notices.Add(new SquadPlayerNotice(lead.Name, 6, "遠征隊已經結束了，由於沒有在正確的頻道裡。"));
                notices.Add(new SquadMapNotice(old.BeginMapId, next.Name + "遠征隊已經結束了，由於有成員沒有在地圖內"));
                lock (_gate) { results.Add((next.Name, "不在地圖內")); }
                continue;
            }

            if (Register(old.Type, lead, old.ExpirationMs / 60000, old.ToSay, out var registered))
            {
                // Java copy()：clock 用 expiration/1000 秒、公告「名字＋toSay」。
                notices.Add(new SquadMapClock(old.BeginMapId, old.ExpirationMs / 1000));
                notices.Add(new SquadMapNotice(old.BeginMapId, next.Name + old.ToSay));
                lock (_gate) { results.Add((next.Name, "成功")); }
            }
            else
            {
                lock (_gate) { results.Add((next.Name, "跳過")); }
            }

            return;
        }
    }

    private bool IsQueued(string type, string name) => Queue(type).Any(q => q.Name == name);

    private List<(string Name, DateTimeOffset Time)> Queue(string key)
    {
        if (!_queues.TryGetValue(key, out var queue))
        {
            _queues[key] = queue = [];
        }

        return queue;
    }

    private List<(string Name, string Result)> Results(string key)
    {
        lock (_gate)
        {
            if (!_queuedResults.TryGetValue(key, out var list))
            {
                _queuedResults[key] = list = [];
            }

            return list;
        }
    }
}
