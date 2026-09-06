using Maple.Application.OnlinePlayers;
using Maple.Core.Characters;
using Maple.Core.Parties;

namespace Maple.Application.Parties;

/// <summary>
/// 對照 Java <c>PartyHandler.PartySearchJob</c> 的職業篩選遮罩位元。部分位元的標籤與實際判斷
/// 不一致（如 <see cref="Buccaneer"/> 對應 Java 標籤「格鬥家」卻檢查 <c>is拳霸</c>），以及
/// <c>0x40</c>（Java「龍騎士」）在 Java <c>checkJob</c> 從未被任何分支使用——皆為舊碼固有行為，
/// 原樣保留，不修正。
/// </summary>
[Flags]
public enum PartySearchJobFilter
{
    None = 0,
    AllJobs = 0x1,
    Beginner = 0x2,
    WildHunter = 0x4,
    Warrior = 0x8,
    Hero = 0x10,
    Knight = 0x20,
    DawnWarrior = 0x80,
    Magician = 0x100,
    FpArchMage = 0x200,
    IlArchMage = 0x400,
    Bishop = 0x800,
    BlazeWizard = 0x1000,
    Pirate = 0x2000,
    Buccaneer = 0x4000,
    Corsair = 0x8000,
    ThunderBreaker = 0x10000,
    Thief = 0x20000,
    NightLord = 0x40000,
    Shadower = 0x80000,
    NightWalker = 0x100000,
    Bowman = 0x200000,
    Bowmaster = 0x400000,
    Marksman = 0x800000,
    WindArcher = 0x1000000,
}

/// <summary>對照 Java <c>World.PartySearch.PartySearchInfo</c>。</summary>
public sealed record PartySearchCriteria(int MinLevel, int MaxLevel, int MemberNum, int JobMask)
{
    public bool IsInLevelRange(int level) => level >= MinLevel && level <= MaxLevel;

    /// <summary>對照 Java <c>PartySearchJob.checkJob</c>。</summary>
    public bool AllowsJob(int job)
    {
        var filter = (PartySearchJobFilter)JobMask;
        bool Has(PartySearchJobFilter f) => (filter & f) == f;

        return Has(PartySearchJobFilter.AllJobs)
            || (Has(PartySearchJobFilter.Beginner) && MapleJobFamily.IsBeginner(job) && !MapleJobFamily.IsWildHunter(job))
            || (Has(PartySearchJobFilter.WildHunter) && MapleJobFamily.IsWildHunter(job))
            || (Has(PartySearchJobFilter.Warrior) && MapleJobFamily.IsWarrior(job) && !MapleJobFamily.IsWildHunter(job))
            || (Has(PartySearchJobFilter.Hero) && MapleJobFamily.IsHero(job))
            || (Has(PartySearchJobFilter.Knight) && (MapleJobFamily.IsPaladin(job) || MapleJobFamily.IsDarkKnight(job)))
            || (Has(PartySearchJobFilter.DawnWarrior) && MapleJobFamily.IsDawnWarrior(job))
            || (Has(PartySearchJobFilter.Magician) && MapleJobFamily.IsMagician(job))
            || (Has(PartySearchJobFilter.FpArchMage) && MapleJobFamily.IsFpArchMage(job))
            || (Has(PartySearchJobFilter.IlArchMage) && MapleJobFamily.IsIlArchMage(job))
            || (Has(PartySearchJobFilter.Bishop) && MapleJobFamily.IsBishop(job))
            || (Has(PartySearchJobFilter.BlazeWizard) && MapleJobFamily.IsBlazeWizard(job))
            || (Has(PartySearchJobFilter.Pirate) && MapleJobFamily.IsPirate(job))
            || (Has(PartySearchJobFilter.Buccaneer) && MapleJobFamily.IsBuccaneer(job))
            || (Has(PartySearchJobFilter.Corsair) && MapleJobFamily.IsCorsair(job))
            || (Has(PartySearchJobFilter.ThunderBreaker) && MapleJobFamily.IsThunderBreaker(job))
            || (Has(PartySearchJobFilter.Thief) && MapleJobFamily.IsThief(job))
            || (Has(PartySearchJobFilter.NightLord) && MapleJobFamily.IsNightLord(job))
            || (Has(PartySearchJobFilter.Shadower) && MapleJobFamily.IsShadower(job))
            || (Has(PartySearchJobFilter.NightWalker) && MapleJobFamily.IsNightWalker(job))
            || (Has(PartySearchJobFilter.Bowman) && MapleJobFamily.IsBowman(job))
            || (Has(PartySearchJobFilter.Bowmaster) && MapleJobFamily.IsBowmaster(job))
            || (Has(PartySearchJobFilter.Marksman) && MapleJobFamily.IsMarksman(job))
            || (Has(PartySearchJobFilter.WindArcher) && MapleJobFamily.IsWindArcher(job));
    }
}

/// <summary>對照 Java <c>World.PartySearch</c> 的搜尋登記表。DI singleton，非 static 可變狀態。</summary>
public interface IPartySearchRegistry
{
    void StartSearch(int leaderCharacterId, PartySearchCriteria criteria);

    void StopSearch(int leaderCharacterId);

    /// <summary>目前所有活躍搜尋的快照（複本，呼叫端可安全迭代）。</summary>
    IReadOnlyList<(int LeaderCharacterId, PartySearchCriteria Criteria)> GetActiveSearches();
}

public sealed class InMemoryPartySearchRegistry : IPartySearchRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<int, PartySearchCriteria> _searches = new();

    public void StartSearch(int leaderCharacterId, PartySearchCriteria criteria)
    {
        lock (_gate)
        {
            _searches[leaderCharacterId] = criteria;
        }
    }

    public void StopSearch(int leaderCharacterId)
    {
        lock (_gate)
        {
            _searches.Remove(leaderCharacterId);
        }
    }

    public IReadOnlyList<(int LeaderCharacterId, PartySearchCriteria Criteria)> GetActiveSearches()
    {
        lock (_gate)
        {
            return _searches.Select(kv => (kv.Key, kv.Value)).ToList();
        }
    }
}

public enum PartySearchStartError
{
    NotLeader,
    MinAboveMax,
    LevelBelowZero,
    LevelAboveCap,
    RangeTooWide,
    SelfOutOfRange,
    MemberCountOutOfRange,
    PartyAlreadyAtSize,
    NoJobSelected,
}

public sealed record PartySearchStartOutcome(bool Succeeded, PartySearchStartError? Error = null, string? RejectionMessage = null)
{
    public static PartySearchStartOutcome Ok() => new(true);

    public static PartySearchStartOutcome Fail(PartySearchStartError error, string message) =>
        new(false, error, message);
}

/// <summary>
/// 對照 Java <c>PartyHandler.PartySearchStart/PartySearchStop</c> + <c>World.PartySearch</c>。
/// 配對判斷改用 <see cref="IOnlinePlayerRegistry"/> 的即時角色狀態，不依賴
/// <see cref="PartyMember"/> 內從未被刷新的 stale MapId/Level/JobId（既有技術債，見任務歷程）。
/// </summary>
public sealed class PartySearchService
{
    private readonly IPartySearchRegistry _registry;
    private readonly IPartyRegistry _parties;
    private readonly IOnlinePlayerRegistry _online;

    public PartySearchService(IPartySearchRegistry registry, IPartyRegistry parties, IOnlinePlayerRegistry online)
    {
        _registry = registry;
        _parties = parties;
        _online = online;
    }

    public PartySearchStartOutcome TryStartSearch(int leaderCharacterId, int leaderLevel, int minLevel, int maxLevel, int memberNum, int jobMask)
    {
        var party = _parties.GetPartyForCharacter(leaderCharacterId);
        if (party is null || party.LeaderId != leaderCharacterId)
        {
            return PartySearchStartOutcome.Fail(PartySearchStartError.NotLeader, "您並非隊伍的隊長！");
        }

        if (minLevel > maxLevel)
        {
            return PartySearchStartOutcome.Fail(PartySearchStartError.MinAboveMax, "搜尋等級範圍的下限高出上限！請重新確認！");
        }

        if (minLevel < 0)
        {
            return PartySearchStartOutcome.Fail(PartySearchStartError.LevelBelowZero, "等級異常！");
        }

        if (maxLevel > 200)
        {
            return PartySearchStartOutcome.Fail(PartySearchStartError.LevelAboveCap, "目前楓之谷的等級上限為200級！");
        }

        if (maxLevel - minLevel > 30)
        {
            return PartySearchStartOutcome.Fail(PartySearchStartError.RangeTooWide, "等級範圍最多可設定到30級！");
        }

        if (minLevel > leaderLevel)
        {
            return PartySearchStartOutcome.Fail(PartySearchStartError.SelfOutOfRange, "所要搜尋的等級範圍中，必須包含自己的等級。");
        }

        if (memberNum < 2 || memberNum > 6)
        {
            return PartySearchStartOutcome.Fail(PartySearchStartError.MemberCountOutOfRange, "隊員最多輸入到2~6人！");
        }

        if (party.Members.Count >= memberNum)
        {
            return PartySearchStartOutcome.Fail(PartySearchStartError.PartyAlreadyAtSize, $"隊員已達到{memberNum}人以上");
        }

        if (jobMask == 0)
        {
            return PartySearchStartOutcome.Fail(PartySearchStartError.NoJobSelected, "請選擇想要組成隊伍的角色職業！");
        }

        _registry.StopSearch(leaderCharacterId);
        _registry.StartSearch(leaderCharacterId, new PartySearchCriteria(minLevel, maxLevel, memberNum, jobMask));
        return PartySearchStartOutcome.Ok();
    }

    public void StopSearch(int characterId) => _registry.StopSearch(characterId);

    /// <summary>
    /// 對照 Java <c>World.PartySearch.checkPartySearch(chr)</c>：候選人（剛進場、或搜尋剛啟動時掃過的同圖玩家）
    /// 是否符合某個活躍搜尋。找到第一筆滿人或第一筆相符即回傳（原樣保留 Java 的 early-return 語意，
    /// 不會一次回傳所有可能配對）。回傳相符搜尋隊長所在的隊伍（供呼叫端組出邀請封包）。
    /// </summary>
    public PartyState? CheckOnMapEntry(int candidateCharacterId, int candidateLevel, int candidateJob, int candidateMapId)
    {
        if (_parties.IsCharacterInParty(candidateCharacterId))
        {
            return null;
        }

        foreach (var (leaderCharacterId, criteria) in _registry.GetActiveSearches())
        {
            var party = _parties.GetPartyForCharacter(leaderCharacterId);
            if (party is null)
            {
                continue;
            }

            if (party.Members.Count >= criteria.MemberNum || party.Members.Count >= 6)
            {
                _registry.StopSearch(leaderCharacterId);
                return null;
            }

            var leaderOnline = _online.FindById(leaderCharacterId);
            if (leaderOnline is null || leaderOnline.Character.MapId != candidateMapId)
            {
                continue;
            }

            if (!criteria.IsInLevelRange(candidateLevel) || !criteria.AllowsJob(candidateJob))
            {
                continue;
            }

            if (party.Members.Count + 1 >= criteria.MemberNum || party.Members.Count >= 6)
            {
                _registry.StopSearch(leaderCharacterId);
            }

            return party;
        }

        return null;
    }
}
