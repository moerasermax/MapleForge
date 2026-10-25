using Maple.Application.Squads;
using Maple.Core.Squads;

namespace Maple.Application.Tests.Squads;

/// <summary>P189：遠征隊（對照 Java MapleSquad、ChannelServer.addMapleSquad、NPCConversationManager.registerSquad）。</summary>
public sealed class SquadRegistryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Register_OnlyValidAndOnce_BroadcastsClockAndNotice()
    {
        var (registry, _, _) = Create(Leader);

        Assert.False(registry.Register("NotASquad", Leader, 5, " 開團", out _));
        Assert.True(registry.Register("PinkBean", Leader, 5, " 開團", out var notices));
        Assert.False(registry.Register("pinkbean", Leader, 5, " 開團", out _));

        Assert.Equal([new SquadMapClock(270050000, 300), new SquadMapNotice(270050000, "Lead 開團")], notices);
        var squad = registry.Get("PINKBEAN")!;
        Assert.Equal(["Lead"], squad.Members);
        Assert.Equal(1, squad.Status);
    }

    [Fact]
    public void AddMember_JoinLeaveFull_NotifiesLeader()
    {
        var member = new SquadCharacter(2, "Mem", 112, 270050000, 0);
        var (registry, _, _) = Create(Leader, member);
        registry.Register("PinkBean", Leader, 5, "", out _);
        var notices = new List<SquadNotice>();

        Assert.Equal(1, registry.AddMember("PinkBean", member, join: true, notices));
        Assert.Equal(-1, registry.AddMember("PinkBean", member, join: true, notices)); // 已在名單
        Assert.Equal(new SquadPlayerNotice("Lead", 6, "Mem (英雄) 加入了遠征隊!"), notices[0]);
        Assert.Equal(1, registry.AddMember("PinkBean", member, join: false, notices));
        Assert.Equal(-1, registry.AddMember("PinkBean", member, join: false, notices));

        var squad = registry.Get("PinkBean")!;
        for (var i = 0; i < 30; i++)
        {
            squad.AddMember($"X{i}", 100, join: true, queued: false);
        }

        Assert.Equal(2, squad.AddMember("Late", 100, join: true, queued: false)); // 31 人後滿
    }

    [Fact]
    public void AddMember_LeaderOffline_ClearsSquad()
    {
        var (registry, online, _) = Create(Leader);
        registry.Register("Zak", Leader, 5, "", out _);
        online.Remove("Lead");
        var notices = new List<SquadNotice>();

        Assert.Equal(-1, registry.AddMember("Zak", new SquadCharacter(3, "Other", 0, 0, 0), true, notices));

        Assert.Null(registry.Get("Zak"));
        Assert.Equal([new SquadMapStopClock(270050000)], notices);
    }

    [Fact]
    public void BanAndAccept_ByPosition_LeaderCannotBeBanned()
    {
        var member = new SquadCharacter(2, "Mem", 0, 270050000, 0);
        var (registry, _, _) = Create(Leader, member);
        registry.Register("Zak", Leader, 5, "", out _);
        var notices = new List<SquadNotice>();
        registry.AddMember("Zak", member, true, notices);
        notices.Clear();

        registry.BanMember("Zak", 0, notices); // 隊長
        Assert.Empty(notices);
        registry.BanMember("Zak", 1, notices);
        var squad = registry.Get("Zak")!;
        Assert.True(squad.IsBanned("Mem"));
        Assert.Equal(new SquadPlayerNotice("Mem", 5, "Lead 從遠征隊中刪除您."), Assert.Single(notices));

        registry.AcceptMember("Zak", 0, notices);
        Assert.Equal(["Lead", "Mem"], squad.Members);
        Assert.Contains("#L0#", squad.MemberString(1));
        Assert.StartsWith("目前遠征隊總人數 : #b2 #k", squad.MemberString(0));
    }

    [Fact]
    public void Tick_ExpiresRecruitingSquad_HandsOverToQueuedPlayerInBeginMap()
    {
        var next = new SquadCharacter(5, "Next", 0, 270050000, 0);
        var (registry, _, time) = Create(Leader, next);
        registry.Register("Zak", Leader, 5, " 開團", out _);
        registry.SetNextPlayer("Zak", "Next");
        Assert.Contains("1 : Next", registry.NextPlayerString("Zak"));

        time.Now = T0.AddMinutes(4);
        Assert.Empty(registry.Tick(0));
        time.Now = T0.AddMinutes(5);
        var notices = registry.Tick(0);

        Assert.Equal("Next", registry.Get("Zak")!.LeaderName);
        Assert.Contains(new SquadMapStopClock(270050000), notices);
        Assert.Contains(new SquadMapNotice(270050000, "Next 開團"), notices);
        Assert.Empty(registry.GetQueue("Zak"));
    }

    [Fact]
    public void Tick_StartedSquad_DoesNotExpire()
    {
        var (registry, _, time) = Create(Leader);
        registry.Register("Zak", Leader, 5, "", out _);
        registry.Get("Zak")!.SetStatus(2);

        time.Now = T0.AddHours(1);

        Assert.Empty(registry.Tick(0));
        Assert.NotNull(registry.Get("Zak"));
    }

    [Fact]
    public void JobNames_MatchJava()
    {
        Assert.Equal("暗影神偷", JobNames.Of(422));
        Assert.Equal("龍魔導士", JobNames.Of(2215));
        Assert.Equal("未知", JobNames.Of(9999));
    }

    private static readonly SquadCharacter Leader = new(1, "Lead", 0, 270050000, 0);

    private static (SquadRegistry, Dictionary<string, SquadCharacter>, ManualTime) Create(params SquadCharacter[] online)
    {
        var map = online.ToDictionary(static c => c.Name);
        var time = new ManualTime { Now = T0 };
        return (new SquadRegistry(n => map.GetValueOrDefault(n), time), map, time);
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
