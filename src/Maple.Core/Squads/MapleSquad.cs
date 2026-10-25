using System.Text;

namespace Maple.Core.Squads;

/// <summary>
/// P189：Java <c>MapleSquad</c>（遠征隊）。成員以角色名為鍵、保留加入順序（第一位＝隊長）；被踢出者進黑名單。
/// 狀態：1 招募中、2 已開戰（取消到期移除）、0 已清除。是否在線由呼叫端以 <c>isOnline</c> 判斷（Java <c>getChar(name)</c>）。
/// </summary>
public sealed class MapleSquad
{
    /// <summary>Java <c>MapleSquadType</c> 的合法名稱（小寫比對）。</summary>
    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "bossbalrog", "zak", "chaoszak", "horntail", "chaosht", "pinkbean", "nmm_squad", "vergamot", "dunas",
        "nibergen_squad", "dunas2", "core_blaze", "aufheben", "cwkpq", "vonleon", "scartar", "cygnus",
    };

    /// <summary>Java addMember：<c>members.size() &lt;= 30</c> 才能再加（實際上限 31）。</summary>
    public const int MaxMembersBeforeFull = 30;

    private readonly List<(string Name, string Job)> _members = [];
    private readonly List<(string Name, string Job)> _banned = [];

    public MapleSquad(string type, int leaderId, string leaderName, int leaderJob, int beginMapId, DateTimeOffset startTime, int expirationMs, string toSay)
    {
        Type = NormalizeType(type);
        LeaderId = leaderId;
        LeaderName = leaderName;
        BeginMapId = beginMapId;
        StartTime = startTime;
        ExpirationMs = expirationMs;
        ToSay = toSay;
        Status = 1;
        _members.Add((leaderName, JobNames.Of(leaderJob)));
    }

    public string Type { get; }

    public int LeaderId { get; }

    public string LeaderName { get; }

    public int BeginMapId { get; }

    public DateTimeOffset StartTime { get; }

    public int ExpirationMs { get; }

    public string ToSay { get; }

    public byte Status { get; private set; }

    public IReadOnlyList<string> Members => _members.Select(static m => m.Name).ToArray();

    public IReadOnlyList<string> BannedMembers => _banned.Select(static m => m.Name).ToArray();

    public static string NormalizeType(string type) => type.ToLowerInvariant();

    public static bool IsValidType(string type) => Types.Contains(type);

    public long TimeLeftMs(DateTimeOffset now) => ExpirationMs - (long)(now - StartTime).TotalMilliseconds;

    public bool ContainsMember(string name) => _members.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool IsBanned(string name) => _banned.Any(m => m.Name == name);

    /// <summary>
    /// Java addMember（隊長在線由呼叫端先檢查，否則 −1）：加入 → 未在名單且未排隊、人數 ≤ 30 則 1，滿了 2，否則 −1；
    /// 退出 → 在名單則 1，否則 −1。
    /// </summary>
    public int AddMember(string name, int job, bool join, bool queued)
    {
        if (join)
        {
            if (ContainsMember(name) || queued)
            {
                return -1;
            }

            if (_members.Count > MaxMembersBeforeFull)
            {
                return 2;
            }

            _members.Add((name, JobNames.Of(job)));
            return 1;
        }

        var index = _members.FindIndex(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return -1;
        }

        _members.RemoveAt(index);
        return 1;
    }

    /// <summary>Java banMember(pos)：pos 1..（不能踢隊長），被踢者需在線；回傳被踢的名字。</summary>
    public string? BanMember(int pos, Func<string, bool> isOnline)
    {
        if (pos <= 0 || pos >= _members.Count || !isOnline(_members[pos].Name))
        {
            return null;
        }

        var member = _members[pos];
        _members.RemoveAt(pos);
        _banned.Add(member);
        return member.Name;
    }

    /// <summary>Java acceptMember(pos)：黑名單第 pos 位（需在線）回到名單；回傳名字。</summary>
    public string? AcceptMember(int pos, Func<string, bool> isOnline)
    {
        if (pos < 0 || pos >= _banned.Count || !isOnline(_banned[pos].Name))
        {
            return null;
        }

        var member = _banned[pos];
        _banned.RemoveAt(pos);
        _members.Add(member);
        return member.Name;
    }

    /// <summary>Java reAddMember：先移除再加回（排到最後）。</summary>
    public void ReAddMember(string name, int job)
    {
        RemoveMember(name);
        _members.Add((name, JobNames.Of(job)));
    }

    public void RemoveMember(string name) => _members.RemoveAll(m => m.Name == name);

    public void RemoveAllMembers() => _members.Clear();

    public void SetStatus(byte status) => Status = status;

    /// <summary>Java clear()：清名單與黑名單、狀態 0。</summary>
    public void Clear()
    {
        _members.Clear();
        _banned.Clear();
        Status = 0;
    }

    public IReadOnlyDictionary<string, int> GetJobs()
    {
        var jobs = new Dictionary<string, int>();
        foreach (var (_, job) in _members)
        {
            jobs[job] = jobs.GetValueOrDefault(job) + 1;
        }

        return jobs;
    }

    /// <summary>Java getSquadMemberString(type)：0 名單、1 名單（可選，踢人用）、2 黑名單（可選）、3 職業統計。</summary>
    public string? MemberString(int type)
    {
        switch (type)
        {
            case 0:
            case 1:
            case 2:
            {
                var sb = new StringBuilder("目前遠征隊總人數 : ");
                sb.Append("#b").Append(_members.Count).Append(" #k ").Append("\r\n遠征隊名單 : \n\r ");
                var list = type == 2 ? _banned : _members;
                var i = 0;
                foreach (var (name, job) in list)
                {
                    if (type != 0)
                    {
                        sb.Append("#b#L").Append(i).Append('#');
                    }

                    i++;
                    sb.Append(i).Append(" : ").Append(name).Append(" (").Append(job).Append(") ");
                    if (i == 1 && type != 2)
                    {
                        sb.Append("(遠征隊領袖)");
                    }

                    if (type != 0)
                    {
                        sb.Append("#l");
                    }

                    sb.Append(" \n\r ");
                }

                while (i < 30)
                {
                    i++;
                    sb.Append(i).Append(" : ").Append(" \n\r ");
                }

                return sb.ToString();
            }

            case 3:
            {
                var sb = new StringBuilder("職業 : ");
                foreach (var (job, count) in GetJobs())
                {
                    sb.Append("\r\n").Append(job).Append(" : ").Append(count);
                }

                return sb.ToString();
            }
        }

        return null;
    }
}
