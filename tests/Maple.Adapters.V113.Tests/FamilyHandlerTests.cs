using Maple.Adapters.V113.Channel;
using Maple.Application.Families;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class FamilyHandlerTests
{
    [Fact]
    public async Task InviteAndAccept_CreatesFamilyAndSendsJoinPackets()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 30);
        var junior = Player(2, "Junior", level: 20);
        harness.Hook.Put(senior);
        harness.Hook.Put(junior);

        var invite = await harness.Handler.HandleFamilyOperationAsync(
            Reader(new PacketWriter().WriteMapleString("Junior")),
            senior,
            CancellationToken.None);

        Assert.True(invite.Succeeded);
        var invitePacket = Assert.Single(harness.Hook.SentPackets[junior.Character.Id]);
        var inviteReader = new PacketReader(invitePacket);
        Assert.Equal(V113FamilyPackets.SendFamilyJoinRequest, inviteReader.ReadShort());
        Assert.Equal(senior.Character.Id, inviteReader.ReadInt());
        Assert.Equal("Senior", inviteReader.ReadMapleString());

        var accept = await harness.Handler.HandleAcceptFamilyAsync(
            Reader(new PacketWriter().WriteInt(senior.Character.Id).WriteMapleString("Senior").WriteByte(1)),
            junior,
            CancellationToken.None);

        Assert.True(accept.Succeeded);
        Assert.True(senior.Character.FamilyId > 0);
        Assert.Equal(senior.Character.FamilyId, junior.Character.FamilyId);
        Assert.Equal(junior.Character.Id, senior.Character.Junior1);
        Assert.Equal(senior.Character.Id, junior.Character.SeniorId);
        Assert.Contains(accept.SelfPackets, packet => PacketOpcode(packet) == V113FamilyPackets.SendFamilyJoinAccepted);
        Assert.Contains(accept.SelfPackets, packet => PacketOpcode(packet) == V113FamilyPackets.SendFamilyInfoResult);

        var response = Assert.Single(harness.Hook.SentPackets[senior.Character.Id]);
        var responseReader = new PacketReader(response);
        Assert.Equal(V113FamilyPackets.SendFamilyJunior, responseReader.ReadShort());
        Assert.Equal(1, responseReader.ReadByte());
        Assert.Equal("Junior", responseReader.ReadMapleString());
    }

    [Fact]
    public async Task DeleteJunior_DetachesJuniorAndKeepsRemainingBranch()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 35);
        var junior1 = Player(2, "JuniorA", level: 25);
        var junior2 = Player(3, "JuniorB", level: 24);
        await JoinAsync(harness, senior, junior1);
        await JoinAsync(harness, senior, junior2);

        var result = await harness.Handler.HandleDeleteJuniorAsync(
            Reader(new PacketWriter().WriteInt(junior1.Character.Id)),
            senior,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, junior1.Character.FamilyId);
        Assert.Equal(0, junior1.Character.SeniorId);
        Assert.Equal(junior2.Character.Id, senior.Character.Junior1);
        Assert.Equal(senior.Character.Id, junior2.Character.SeniorId);
        Assert.Equal(senior.Character.FamilyId, junior2.Character.FamilyId);
    }

    [Fact]
    public async Task DeleteSenior_LeavesSeniorAndClearsSingleMemberFamily()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 30);
        var junior = Player(2, "Junior", level: 20);
        await JoinAsync(harness, senior, junior);

        var result = await harness.Handler.HandleDeleteSeniorAsync(
            new PacketReader(Array.Empty<byte>()),
            junior,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, junior.Character.FamilyId);
        Assert.Equal(0, junior.Character.SeniorId);
        Assert.Equal(0, senior.Character.FamilyId);
        Assert.Equal(0, senior.Character.Junior1);
    }

    [Fact]
    public async Task UseFamilyBuff_SpendsRepAndWritesChangeRepPacket()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 30, currentRep: 1000, totalRep: 1000);
        var junior = Player(2, "Junior", level: 20);
        await JoinAsync(harness, senior, junior);

        var result = await harness.Handler.HandleUseFamilyAsync(
            Reader(new PacketWriter().WriteInt(2)),
            senior,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(700, senior.Character.CurrentRep);
        var changeRepPacket = Assert.Single(result.SelfPackets, packet => PacketOpcode(packet) == V113FamilyPackets.SendFamilyFamousPointIncResult);
        var reader = new PacketReader(changeRepPacket);
        reader.ReadShort();
        Assert.Equal(-300, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
    }

    [Fact]
    public async Task FamilyPrecept_LeaderSetsNotice()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 30);
        var junior = Player(2, "Junior", level: 20);
        await JoinAsync(harness, senior, junior);

        var result = await harness.Handler.HandleFamilyPreceptAsync(
            Reader(new PacketWriter().WriteMapleString("notice")),
            senior,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains(result.SelfPackets, packet => PacketOpcode(packet) == V113FamilyPackets.SendFamilyInfoResult);
        var info = harness.Service.GetFamilyInfo(senior.Character.Id);
        Assert.Equal("notice", info.Notice);
    }

    [Fact]
    public async Task NotifyLoginAsync_MarksMemberOnlineAndBroadcastsFamilyLoggedInToPedigree()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 30);
        var junior = Player(2, "Junior", level: 20);
        await JoinAsync(harness, senior, junior);
        // JoinAsync 已透過 HandleFamilyOperationAsync/HandleAcceptFamilyAsync 呼叫過 _families.Register，
        // 這裡先 Unregister 模擬「junior 剛登入前，帳號從未觸發過任何家族 opcode」的真實情境。
        harness.Service.Unregister(junior.Character.Id);
        harness.Hook.ClearSent();

        var beforeLogin = harness.Service.GetFamilyPedigree(senior.Character.Id);
        Assert.Contains(beforeLogin.Members, m => m.CharacterId == junior.Character.Id && !m.IsOnline);

        // 對照 Java InterServerHandler 登入流程 World.Family.setFamilyMemberOnline(chrf, true, channel)
        // ＋ MapleFamily.setOnline 的 familyLoggedIn 廣播：登入當下就要同步線上狀態並通知族譜可視範圍
        // 內的其他在線成員（junior 不是 leader，故只通知自己的族譜範圍，這裡即 senior）。
        await harness.Handler.NotifyLoginAsync(junior, channel: 3, CancellationToken.None);

        var afterLogin = harness.Service.GetFamilyPedigree(senior.Character.Id);
        var juniorEntry = Assert.Single(afterLogin.Members, m => m.CharacterId == junior.Character.Id);
        Assert.True(juniorEntry.IsOnline);
        Assert.Equal(3, juniorEntry.Channel);

        var notice = Assert.Single(harness.Hook.SentPackets[senior.Character.Id]);
        var reader = new PacketReader(notice);
        Assert.Equal(V113FamilyPackets.SendFamilyNotifyLoginOrLogout, reader.ReadShort());
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal("Junior", reader.ReadMapleString());
    }

    [Fact]
    public async Task NotifyDisconnectAsync_ClearsOnlineStatusAndBroadcastsFamilyLoggedOutToPedigree()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 30);
        var junior = Player(2, "Junior", level: 20);
        await JoinAsync(harness, senior, junior);
        await harness.Handler.NotifyLoginAsync(junior, channel: 3, CancellationToken.None);
        harness.Hook.ClearSent();

        // 對照 Java MapleClient.disconnect() 的 World.Family.setFamilyMemberOnline(chrf, false, -1)
        // ＋ MapleFamily.setOnline 的 familyLoggedIn 廣播：斷線要清除線上狀態並通知族譜範圍內的
        // 其他在線成員，否則其他成員的族譜視圖會永遠顯示線上。
        await harness.Handler.NotifyDisconnectAsync(junior, CancellationToken.None);

        var afterDisconnect = harness.Service.GetFamilyPedigree(senior.Character.Id);
        var juniorEntry = Assert.Single(afterDisconnect.Members, m => m.CharacterId == junior.Character.Id);
        Assert.False(juniorEntry.IsOnline);
        Assert.Equal(-1, juniorEntry.Channel);

        var notice = Assert.Single(harness.Hook.SentPackets[senior.Character.Id]);
        var reader = new PacketReader(notice);
        Assert.Equal(V113FamilyPackets.SendFamilyNotifyLoginOrLogout, reader.ReadShort());
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal("Junior", reader.ReadMapleString());
    }

    [Fact]
    public async Task NotifyLoginAsync_MemberAlreadyOnline_DoesNotBroadcastAgain()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 30);
        var junior = Player(2, "Junior", level: 20);
        await JoinAsync(harness, senior, junior);
        harness.Hook.ClearSent();

        // JoinAsync 內部流程已經呼叫過 _families.Register(junior)（等同已在線），此時再收到一次
        // 登入通知（例如換頻道流程）不應該重複廣播——對照 Java `if (mgc.isOnline() != online)` 只有
        // 狀態真的翻轉才廣播的守門判斷。
        await harness.Handler.NotifyLoginAsync(junior, channel: 5, CancellationToken.None);

        Assert.False(harness.Hook.SentPackets.ContainsKey(senior.Character.Id));
    }

    private static async Task JoinAsync(Harness harness, Player senior, Player junior)
    {
        harness.Hook.Put(senior);
        harness.Hook.Put(junior);
        await harness.Handler.HandleFamilyOperationAsync(
            Reader(new PacketWriter().WriteMapleString(junior.Character.Name)),
            senior,
            CancellationToken.None);
        await harness.Handler.HandleAcceptFamilyAsync(
            Reader(new PacketWriter().WriteInt(senior.Character.Id).WriteMapleString(senior.Character.Name).WriteByte(1)),
            junior,
            CancellationToken.None);
        harness.Hook.ClearSent();
    }

    private static Harness NewHarness()
    {
        var service = new FamilyService(new InMemoryFamilyRepository());
        var hook = new FakeFamilySessionHook();
        var handler = new V113FamilyHandler(service, hook);
        return new Harness(service, hook, handler);
    }

    private static PacketReader Reader(PacketWriter writer) => new(writer.ToArray());

    private static short PacketOpcode(byte[] packet) => new PacketReader(packet).ReadShort();

    private static Player Player(int id, string name, byte level, int mapId = 100000000, int currentRep = 0, int totalRep = 0) =>
        new(
            new Character
            {
                Id = id,
                Name = name,
                Level = level,
                Job = 100,
                MapId = mapId,
                CurrentRep = currentRep,
                TotalRep = totalRep,
            },
            new Position(0, 0, 0, 0));

    private sealed record Harness(FamilyService Service, FakeFamilySessionHook Hook, V113FamilyHandler Handler);

    private sealed class FakeFamilySessionHook : IV113FamilySessionHook
    {
        private readonly Dictionary<int, Player> _playersById = new();
        private readonly Dictionary<string, Player> _playersByName = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<int, List<byte[]>> SentPackets { get; } = new();

        public void Put(Player player)
        {
            _playersById[player.Character.Id] = player;
            _playersByName[player.Character.Name] = player;
        }

        public void ClearSent() => SentPackets.Clear();

        public ValueTask<Player?> FindOnlinePlayerByNameAsync(string name, CancellationToken ct) =>
            ValueTask.FromResult(_playersByName.GetValueOrDefault(name));

        public ValueTask<Player?> FindOnlinePlayerByIdAsync(int characterId, CancellationToken ct) =>
            ValueTask.FromResult(_playersById.GetValueOrDefault(characterId));

        public ValueTask SendPacketAsync(int characterId, byte[] packet, CancellationToken ct)
        {
            if (!SentPackets.TryGetValue(characterId, out var packets))
            {
                packets = [];
                SentPackets[characterId] = packets;
            }

            packets.Add(packet);
            return ValueTask.CompletedTask;
        }
    }
}
