using Maple.Application.Guilds;
using Maple.Core.Characters;
using Maple.Core.Guilds;
using Maple.Core.World;

namespace Maple.Application.Tests.Guilds;

public sealed class GuildServiceTests
{
    [Fact]
    public async Task CreateInviteAcceptLeave_TracksGuildAndRecipients()
    {
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 100), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost, mapId: GuildService.CreationMapId);
        var guest = Player(2, "Guest");
        characters.Put(leader.Character);
        characters.Put(guest.Character);

        var created = await service.CreateGuildAsync(leader, "Forge", channel: 1);

        Assert.True(created.Succeeded);
        Assert.Equal(100, created.Guild!.Id);
        Assert.Equal(GuildService.InitialGuildPoints, created.Guild.GuildPoints);
        Assert.Equal(0, leader.Character.Meso);
        Assert.Equal(100, leader.Character.GuildId);
        Assert.Equal(Guild.LeaderRank, leader.Character.GuildRank);
        Assert.Equal(new[] { 1 }, created.Recipients);

        var invite = await service.InviteMemberAsync(leader.Character.Id, GuildMember.FromCharacter(guest.Character, channel: 1));
        Assert.True(invite.Succeeded);

        var joined = await service.AcceptInviteAsync(guest, created.Guild.Id, channel: 1);

        Assert.True(joined.Succeeded);
        Assert.Equal(GuildUpdateKind.Joined, joined.UpdateKind);
        Assert.Equal(new[] { 1, 2 }, joined.Recipients);
        Assert.Equal(100, guest.Character.GuildId);
        Assert.Equal(Guild.DefaultMemberRank, guest.Character.GuildRank);
        Assert.Equal(new[] { 1, 2 }, joined.Guild!.Members.Select(m => m.CharacterId).ToArray());

        var left = await service.LeaveGuildAsync(guest);

        Assert.True(left.Succeeded);
        Assert.Equal(GuildUpdateKind.Left, left.UpdateKind);
        Assert.Equal(new[] { 1, 2 }, left.Recipients);
        Assert.Equal(0, guest.Character.GuildId);
        Assert.Equal(new[] { 1 }, (await service.GetGuildAsync(100))!.Members.Select(m => m.CharacterId).ToArray());
    }

    [Fact]
    public async Task ChangeRankAndExpel_RequireAuthorizedRanksAndUpdateCharacterStatus()
    {
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 10), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost, mapId: GuildService.CreationMapId);
        var junior = Player(2, "Junior");
        var member = Player(3, "Member");
        characters.Put(leader.Character);
        characters.Put(junior.Character);
        characters.Put(member.Character);

        var created = await service.CreateGuildAsync(leader, "Forge", channel: 1);
        await InviteAndJoinAsync(service, leader, junior, created.Guild!.Id);
        await InviteAndJoinAsync(service, leader, member, created.Guild.Id);

        var promoted = await service.ChangeRankAsync(leader, junior.Character.Id, Guild.JuniorMasterRank);

        Assert.True(promoted.Succeeded);
        Assert.Equal(GuildUpdateKind.RankChanged, promoted.UpdateKind);
        Assert.Equal(Guild.JuniorMasterRank, characters.Find(junior.Character.Id)!.GuildRank);

        var rejected = await service.ExpelMemberAsync(member, junior.Character.Id, junior.Character.Name);
        Assert.Equal(GuildCommandStatus.NotAuthorized, rejected.Status);

        var expelled = await service.ExpelMemberAsync(junior, member.Character.Id, member.Character.Name);

        Assert.True(expelled.Succeeded);
        Assert.Equal(GuildUpdateKind.Expelled, expelled.UpdateKind);
        Assert.Equal(0, characters.Find(member.Character.Id)!.GuildId);
        Assert.Equal(new[] { 1, 2, 3 }, expelled.Recipients);
    }

    [Fact]
    public async Task ChangeTitlesEmblemNotice_UpdatesGuildSnapshot()
    {
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 20), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost + GuildService.EmblemCost, mapId: GuildService.CreationMapId);
        characters.Put(leader.Character);

        await service.CreateGuildAsync(leader, "Forge", channel: 1);

        var titles = new[] { "Master", "Jr", "A", "B", "C" };
        var titleResult = await service.ChangeRankTitlesAsync(leader, titles);
        var emblemResult = await service.ChangeEmblemAsync(
            leader,
            new GuildEmblem { LogoBackground = 12, LogoBackgroundColor = 3, Logo = 45, LogoColor = 6 });
        var noticeResult = await service.ChangeNoticeAsync(leader, "hello");

        Assert.True(titleResult.Succeeded);
        Assert.True(emblemResult.Succeeded);
        Assert.True(noticeResult.Succeeded);
        var guild = (await service.GetGuildForCharacterAsync(leader.Character.Id))!;
        Assert.Equal(titles, guild.RankTitles);
        Assert.Equal(12, guild.Emblem.LogoBackground);
        Assert.Equal(45, guild.Emblem.Logo);
        Assert.Equal("hello", guild.Notice);
        Assert.Equal(GuildService.CreationCost + GuildService.EmblemCost - GuildService.CreationCost - GuildService.EmblemCost, leader.Character.Meso);
    }

    [Fact]
    public async Task IncreaseGuildCapacityAsync_BelowCap_IncreasesByFiveAndDeductsMeso()
    {
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 30), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost + GuildService.IncreaseCapacityCost, mapId: GuildService.CreationMapId);
        characters.Put(leader.Character);
        var created = await service.CreateGuildAsync(leader, "Forge", channel: 1);

        var result = await service.IncreaseGuildCapacityAsync(leader);

        Assert.True(result.Succeeded);
        Assert.Equal(GuildUpdateKind.CapacityChanged, result.UpdateKind);
        Assert.Equal(Guild.InitialCapacity + 5, result.Guild!.Capacity);
        Assert.Equal(0, leader.Character.Meso);
        Assert.Equal(new[] { 1 }, result.Recipients);
        Assert.Equal(Guild.InitialCapacity + 5, (await service.GetGuildAsync(created.Guild!.Id))!.Capacity);
    }

    [Fact]
    public async Task IncreaseGuildCapacityAsync_AtCap_StillDeductsMeso_MatchingJavaUncheckedReturnValue()
    {
        // 對照 Java NPCConversationManager.increaseGuildCapacity：World.Guild.increaseGuildCapacity(gid)
        // 的回傳值被忽略，即使公會已達 100 人上限、容量沒真的增加，楓幣依然無條件扣除。
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 40), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost, mapId: GuildService.CreationMapId);
        characters.Put(leader.Character);
        await service.CreateGuildAsync(leader, "Forge", channel: 1);
        var guild = (await service.GetGuildForCharacterAsync(leader.Character.Id))!;
        var atCapGuild = (await guilds.FindByIdAsync(guild.Id))!;
        for (var i = 0; i < 20; i++)
        {
            atCapGuild.TryIncreaseCapacity();
        }
        Assert.Equal(Guild.MaximumCapacity, atCapGuild.Capacity);
        leader.GainMeso(GuildService.IncreaseCapacityCost);

        var result = await service.IncreaseGuildCapacityAsync(leader);

        Assert.False(result.Succeeded);
        Assert.Equal(0, leader.Character.Meso);
        Assert.Equal(Guild.MaximumCapacity, (await service.GetGuildAsync(guild.Id))!.Capacity);
    }

    [Fact]
    public async Task IncreaseGuildCapacityAsync_NoGuild_ReturnsNotInGuild_WithoutDeductingMeso()
    {
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 50), characters);
        var loner = Player(1, "Loner", meso: GuildService.IncreaseCapacityCost);
        characters.Put(loner.Character);

        var result = await service.IncreaseGuildCapacityAsync(loner);

        Assert.Equal(GuildCommandStatus.NotInGuild, result.Status);
        Assert.Equal(GuildService.IncreaseCapacityCost, loner.Character.Meso);
    }

    [Fact]
    public async Task DisbandGuildAsync_Leader_RemovesGuildAndResetsAllMembers()
    {
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 60), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost, mapId: GuildService.CreationMapId);
        var member = Player(2, "Member");
        characters.Put(leader.Character);
        characters.Put(member.Character);
        var created = await service.CreateGuildAsync(leader, "Forge", channel: 1);
        await InviteAndJoinAsync(service, leader, member, created.Guild!.Id);

        var result = await service.DisbandGuildAsync(leader);

        Assert.True(result.Succeeded);
        Assert.Equal(GuildUpdateKind.Disbanded, result.UpdateKind);
        Assert.Equal(new[] { 1, 2 }, result.Recipients);
        Assert.Equal(2, result.Guild!.Members.Count);
        Assert.Null(await service.GetGuildAsync(created.Guild.Id));
        Assert.Equal(0, characters.Find(leader.Character.Id)!.GuildId);
        Assert.Equal(Guild.DefaultMemberRank, characters.Find(leader.Character.Id)!.GuildRank);
        Assert.Equal(0, characters.Find(member.Character.Id)!.GuildId);
        Assert.Equal(Guild.DefaultMemberRank, characters.Find(member.Character.Id)!.GuildRank);
    }

    [Fact]
    public async Task DisbandGuildAsync_NonLeader_ReturnsNotLeader_WithoutMutatingGuild()
    {
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 70), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost, mapId: GuildService.CreationMapId);
        var member = Player(2, "Member");
        characters.Put(leader.Character);
        characters.Put(member.Character);
        var created = await service.CreateGuildAsync(leader, "Forge", channel: 1);
        await InviteAndJoinAsync(service, leader, member, created.Guild!.Id);

        var result = await service.DisbandGuildAsync(member);

        Assert.Equal(GuildCommandStatus.NotLeader, result.Status);
        Assert.NotNull(await service.GetGuildAsync(created.Guild.Id));
        Assert.Equal(created.Guild.Id, characters.Find(member.Character.Id)!.GuildId);
    }

    [Fact]
    public async Task DisbandGuildAsync_NoGuild_ReturnsNotLeader()
    {
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 80), characters);
        var loner = Player(1, "Loner");
        characters.Put(loner.Character);

        var result = await service.DisbandGuildAsync(loner);

        Assert.Equal(GuildCommandStatus.NotLeader, result.Status);
    }

    [Fact]
    public async Task CreateGuildAsync_RepositoryAddFails_DoesNotLeaveCharacterLockedInRegistry()
    {
        // P036：AddAsync 失敗前不能先登記進 registry，否則角色會被 AlreadyInGuild 卡死、
        // 再也建不了公會（registry 認為已有公會，但 DB 其實沒有這筆資料）。
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository { ThrowOnNextAdd = true };
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 90), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost * 2, mapId: GuildService.CreationMapId);
        characters.Put(leader.Character);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateGuildAsync(leader, "Forge", channel: 1));

        // registry 沒有殘留任何登記，重試應該正常成功。
        var retry = await service.CreateGuildAsync(leader, "Forge", channel: 1);
        Assert.True(retry.Succeeded);
    }

    [Fact]
    public async Task DisbandGuildAsync_RepositoryDeleteFails_GuildStillExistsInRegistry()
    {
        // P036：DeleteAsync 失敗前不能先從 registry 移除，否則 process 重啟後公會會從 DB「詐屍」
        // 復活，但成員在這段期間已經以為公會不存在。
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 91), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost, mapId: GuildService.CreationMapId);
        characters.Put(leader.Character);
        var created = await service.CreateGuildAsync(leader, "Forge", channel: 1);
        guilds.ThrowOnNextDelete = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DisbandGuildAsync(leader));

        Assert.NotNull(await service.GetGuildAsync(created.Guild!.Id));
    }

    [Fact]
    public async Task AcceptInviteAsync_RepositoryUpdateFails_DoesNotLeaveTargetLockedInRegistry()
    {
        // P037：AddMemberAsync 的 _guildByCharacter 登記延到 UpdateAsync 成功後才做，
        // 失敗時目標角色不該被誤判為「已在公會」。
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 100), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost, mapId: GuildService.CreationMapId);
        var target = Player(2, "Guest");
        characters.Put(leader.Character);
        characters.Put(target.Character);
        var created = await service.CreateGuildAsync(leader, "Forge", channel: 1);
        await service.InviteMemberAsync(leader.Character.Id, GuildMember.FromCharacter(target.Character, channel: 1));
        guilds.ThrowOnNextUpdate = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptInviteAsync(target, created.Guild!.Id, channel: 1));

        Assert.Null(await service.GetGuildForCharacterAsync(target.Character.Id));
    }

    [Fact]
    public async Task LeaveGuildAsync_RepositoryUpdateFails_CharacterStaysInRegistry()
    {
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 101), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost, mapId: GuildService.CreationMapId);
        var member = Player(2, "Member");
        characters.Put(leader.Character);
        characters.Put(member.Character);
        var created = await service.CreateGuildAsync(leader, "Forge", channel: 1);
        await InviteAndJoinAsync(service, leader, member, created.Guild!.Id);
        guilds.ThrowOnNextUpdate = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LeaveGuildAsync(member));

        // _guildByCharacter（registry 字典）失敗時不該移除——這是這次修的部分。guild.Members
        // 物件欄位本身已經先異動過（較輕微的同步風險，P036/P037 皆刻意不處理，見任務歷程）。
        Assert.NotNull(await service.GetGuildForCharacterAsync(member.Character.Id));
    }

    [Fact]
    public async Task ExpelMemberAsync_RepositoryUpdateFails_TargetStaysInRegistry()
    {
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 102), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost, mapId: GuildService.CreationMapId);
        var member = Player(2, "Member");
        characters.Put(leader.Character);
        characters.Put(member.Character);
        var created = await service.CreateGuildAsync(leader, "Forge", channel: 1);
        await InviteAndJoinAsync(service, leader, member, created.Guild!.Id);
        guilds.ThrowOnNextUpdate = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExpelMemberAsync(leader, member.Character.Id, member.Character.Name));

        // 同上：_guildByCharacter 失敗時不該移除，guild.Members 物件欄位的同步風險不在這次範圍。
        Assert.NotNull(await service.GetGuildForCharacterAsync(member.Character.Id));
    }

    private static async Task InviteAndJoinAsync(GuildService service, Player leader, Player target, int guildId)
    {
        var invite = await service.InviteMemberAsync(leader.Character.Id, GuildMember.FromCharacter(target.Character, channel: 1));
        Assert.True(invite.Succeeded);
        var join = await service.AcceptInviteAsync(target, guildId, channel: 1);
        Assert.True(join.Succeeded);
    }

    private static Player Player(int id, string name, int meso = 0, int mapId = 100000000) =>
        new(new Character
        {
            Id = id,
            Name = name,
            Level = 30,
            Job = 100,
            Meso = meso,
            MapId = mapId,
        }, new Position(0, 0, 0, 0));

    private sealed class FakeGuildRepository : IGuildRepository
    {
        private readonly Dictionary<int, Guild> _guilds = new();

        /// <summary>P036/P037 容錯測試用：下一次對應操作拋例外，模擬 DB 寫入失敗。</summary>
        public bool ThrowOnNextAdd { get; set; }
        public bool ThrowOnNextDelete { get; set; }
        public bool ThrowOnNextUpdate { get; set; }

        public Task<IReadOnlyList<Guild>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guild>>(_guilds.Values.ToList());

        public Task<Guild?> FindByIdAsync(int guildId, CancellationToken ct = default) =>
            Task.FromResult(_guilds.GetValueOrDefault(guildId));

        public Task<Guild?> FindByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(_guilds.Values.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(Guild guild, CancellationToken ct = default)
        {
            if (ThrowOnNextAdd)
            {
                ThrowOnNextAdd = false;
                throw new InvalidOperationException("模擬 DB 寫入失敗");
            }

            _guilds[guild.Id] = guild;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Guild guild, CancellationToken ct = default)
        {
            if (ThrowOnNextUpdate)
            {
                ThrowOnNextUpdate = false;
                throw new InvalidOperationException("模擬 DB 更新失敗");
            }

            _guilds[guild.Id] = guild;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int guildId, CancellationToken ct = default)
        {
            if (ThrowOnNextDelete)
            {
                ThrowOnNextDelete = false;
                throw new InvalidOperationException("模擬 DB 刪除失敗");
            }

            _guilds.Remove(guildId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCharacterRepository : ICharacterRepository
    {
        private readonly Dictionary<int, Character> _characters = new();

        public void Put(Character character) => _characters[character.Id] = character;

        public Character? Find(int characterId) => _characters.GetValueOrDefault(characterId);

        public Task<IReadOnlyList<Character>> GetByAccountAsync(int accountId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Character>>(_characters.Values.Where(c => c.AccountId == accountId).ToList());

        public Task<Character?> FindByIdAsync(int characterId, CancellationToken ct = default) =>
            Task.FromResult(_characters.GetValueOrDefault(characterId));

        public Task<Character?> FindByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(_characters.Values.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(Character character, CancellationToken ct = default)
        {
            Put(character);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Character character, CancellationToken ct = default)
        {
            Put(character);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(int characterId, CancellationToken ct = default) => Task.FromResult(false);
    }
}
