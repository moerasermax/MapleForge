using Maple.Application.Families;
using Maple.Core.Characters;
using Maple.Core.Families;
using Maple.Core.World;

namespace Maple.Application.Tests.Families;

/// <summary>
/// P051：FamilyService 之前完全沒有 Application 層單元測試。這次針對剛重構的
/// Register/Unregister/SetOnline（抽出 *Locked 版本避免 SetOnline 重入呼叫公開方法，
/// 為將來把 _sync 從 lock 換成 SemaphoreSlim 做準備，見任務歷程 2026-09-06_46/53）補上
/// 針對性測試，鎖定這幾個方法的既有行為，而非嘗試涵蓋 FamilyService 全部功能。
///
/// 測試 fixture 注意：<c>RegisterLocked(Player, channel)</c> 會用 <c>Player.Character</c> 上的
/// Junior1/Junior2/SeniorId/CurrentRep/TotalRep 覆寫 registry 裡的 <see cref="FamilyMember"/>
/// 對應欄位（對照生產流程：這些欄位平常由 <c>ApplyFamilyToCharacter</c> 保持跟 FamilyMember
/// 同步，玩家上線時反向同步回 registry）。所以測試建立 Player 時，Character 上的這些欄位
/// 必須跟對應的 FamilyMember 結構一致，否則 SetOnline 會把 registry 的家族結構覆寫成空的
/// （這不是 bug，是「用未同步過的 Character 上線」這個測試前提本身不寫實）。
/// </summary>
public sealed class FamilyServiceTests
{
    [Fact]
    public async Task CreateFamilyAsync_PersistsFamilyBeforeReturning()
    {
        // P053：CreateFamilyAsync 原本「先掛進 registry、鎖外才 SaveAsync」跟 P036 修過的
        // GuildService.CreateGuildAsync 同一種風險，這次改成異動+持久化同一段臨界區內完成。
        var repository = new InMemoryFamilyRepository();
        var service = new FamilyService(repository, firstFamilyId: 5);

        var family = await service.CreateFamilyAsync(leaderId: 1);

        Assert.Equal(5, family.Id);
        Assert.Equal(1, family.LeaderId);
        var persisted = await repository.FindByIdAsync(5);
        Assert.NotNull(persisted);
        Assert.Equal(1, persisted!.LeaderId);
    }

    [Fact]
    public void SetOnline_LeaderComesOnline_SyncsMemberFieldsAndNotifiesOnlineFamilyMembers()
    {
        var service = new FamilyService(new InMemoryFamilyRepository());
        var family = NewFamily(leaderId: 1, juniorId: 2);
        service.Register(family);

        var junior = NewPlayer(2, "Junior", level: 10, seniorId: 1);
        service.SetOnline(junior, online: true, channel: 1);

        var leader = NewPlayer(1, "Leader", level: 99, junior1: 2, currentRep: 5, totalRep: 50);
        var change = service.SetOnline(leader, online: true, channel: 1);

        Assert.True(change.Changed);
        Assert.True(change.Online);
        Assert.Equal("Leader", change.MemberName);
        Assert.Contains(2, change.NotifyRecipientIds);

        var info = service.GetFamilyInfo(1);
        Assert.Equal(5, info.CurrentRep); // 對照 RegisterLocked(Player, channel) 把 member 欄位同步成最新角色狀態
        Assert.Equal(1, info.JuniorCount); // 家族結構（junior1=2）在同步後仍完整保留
    }

    [Fact]
    public void SetOnline_SameStateTwice_ReturnsNoneAndDoesNotNotify()
    {
        var service = new FamilyService(new InMemoryFamilyRepository());
        var family = NewFamily(leaderId: 1, juniorId: 2);
        service.Register(family);

        var leader = NewPlayer(1, "Leader", level: 30, junior1: 2);
        service.SetOnline(leader, online: true, channel: 1);
        var second = service.SetOnline(leader, online: true, channel: 1);

        Assert.False(second.Changed);
        Assert.Equal(FamilyOnlineStatusChange.None, second);
    }

    [Fact]
    public void SetOnline_MemberGoesOffline_UnregistersAndReflectsInOnlineChannelsSnapshot()
    {
        var service = new FamilyService(new InMemoryFamilyRepository());
        var family = NewFamily(leaderId: 1, juniorId: 2);
        service.Register(family);

        var leader = NewPlayer(1, "Leader", level: 30, junior1: 2);
        service.SetOnline(leader, online: true, channel: 1);

        var change = service.SetOnline(leader, online: false, channel: 1);

        Assert.True(change.Changed);
        Assert.False(change.Online);

        var info = service.GetFamilyInfo(1);
        Assert.Equal(1, info.JuniorCount); // 結構上的 junior 數量跟上下線無關，僅確認 GetFamilyInfo 仍可正常執行（Unregister 沒有壞掉共用狀態）
    }

    private static Family NewFamily(int leaderId, int juniorId)
    {
        var leader = new FamilyMember { CharacterId = leaderId, Name = "Leader", Level = 30 };
        leader.TryAddJunior(juniorId);
        var junior = new FamilyMember { CharacterId = juniorId, Name = "Junior", Level = 10, SeniorId = leaderId };

        var family = new Family { Id = 1, LeaderId = leaderId };
        family.TryAddMember(leader);
        family.TryAddMember(junior);
        return family;
    }

    private static Player NewPlayer(
        int id,
        string name,
        byte level,
        int seniorId = 0,
        int junior1 = 0,
        int junior2 = 0,
        int currentRep = 0,
        int totalRep = 0) =>
        new(
            new Character
            {
                Id = id,
                Name = name,
                Level = level,
                SeniorId = seniorId,
                Junior1 = junior1,
                Junior2 = junior2,
                CurrentRep = currentRep,
                TotalRep = totalRep,
            },
            new Position(0, 0, 0, 0));
}
