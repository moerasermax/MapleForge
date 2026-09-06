using Maple.Application.Families;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public sealed record V113FamilyHandleResult(
    FamilyCommandStatus Status,
    IReadOnlyList<byte[]> SelfPackets,
    FamilyWarpTarget? Warp = null)
{
    public bool Succeeded => Status == FamilyCommandStatus.Success;
}

public interface IV113FamilySessionHook
{
    ValueTask<Player?> FindOnlinePlayerByNameAsync(string name, CancellationToken ct);

    ValueTask<Player?> FindOnlinePlayerByIdAsync(int characterId, CancellationToken ct);

    ValueTask SendPacketAsync(int characterId, byte[] packet, CancellationToken ct);
}

public sealed class V113FamilyHandler
{
    private readonly FamilyService _families;
    private readonly IV113FamilySessionHook _sessions;

    public V113FamilyHandler(FamilyService families, IV113FamilySessionHook sessions)
    {
        _families = families;
        _sessions = sessions;
    }

    /// <summary>
    /// 對照 Java <c>InterServerHandler</c> 登入流程 <c>World.Family.setFamilyMemberOnline(chrf, true, channel)</c>：
    /// 家族線上狀態要在登入當下同步，不能等到玩家自己觸發某個家族 opcode（既有 <c>_families.Register</c>
    /// 只散落在各家族操作 handler 內，登入完全沒呼叫，其他成員在此之前查族譜看不到剛上線的人）。
    /// </summary>
    public void NotifyLogin(Player player, int channel) => _families.Register(player, channel);

    /// <summary>
    /// 對照 Java <c>MapleClient.disconnect()</c> 的 <c>World.Family.setFamilyMemberOnline(chrf, false, -1)</c>：
    /// 斷線要清除線上狀態，否則玩家登出後在其他成員的族譜視圖裡會永遠顯示線上（<c>_families.Unregister</c>
    /// 過去在整個專案零呼叫者）。
    /// </summary>
    public void NotifyDisconnect(Player player) => _families.Unregister(player.Character.Id);

    public async Task<V113FamilyHandleResult> HandleRequestFamilyAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        string name;
        try
        {
            name = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return Empty(FamilyCommandStatus.InvalidOperation);
        }

        var target = await _sessions.FindOnlinePlayerByNameAsync(name, ct).ConfigureAwait(false);
        if (target is null)
        {
            return Empty(FamilyCommandStatus.TargetNotFound);
        }

        _families.Register(target);
        return SelfOnly(FamilyCommandStatus.Success, V113FamilyPackets.FamilyPedigree(_families.GetFamilyPedigree(target.Character.Id)));
    }

    public Task<V113FamilyHandleResult> HandleOpenFamilyAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        _families.Register(player);
        return Task.FromResult(SelfOnly(FamilyCommandStatus.Success, V113FamilyPackets.FamilyInfo(_families.GetFamilyInfo(player.Character.Id))));
    }

    public async Task<V113FamilyHandleResult> HandleFamilyOperationAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        string targetName;
        try
        {
            targetName = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return EnableActions(FamilyCommandStatus.InvalidOperation);
        }

        var target = await _sessions.FindOnlinePlayerByNameAsync(targetName, ct).ConfigureAwait(false);
        if (target is null)
        {
            return EnableActions(FamilyCommandStatus.TargetNotFound);
        }

        _families.Register(player);
        _families.Register(target);
        var result = _families.InviteToFamily(player, target);
        if (result.Succeeded)
        {
            await _sessions.SendPacketAsync(
                target.Character.Id,
                V113FamilyPackets.FamilyInvite(player.Character.Id, player.Character.Level, player.Character.Job, player.Character.Name),
                ct).ConfigureAwait(false);
        }

        return new V113FamilyHandleResult(result.Status, [V113StatsPackets.EnableActions()]);
    }

    public async Task<V113FamilyHandleResult> HandleDeleteJuniorAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        int juniorId;
        try
        {
            juniorId = reader.ReadInt();
        }
        catch (InvalidDataException)
        {
            return Empty(FamilyCommandStatus.InvalidOperation);
        }

        _families.Register(player);
        var result = await _families.DeleteJuniorAsync(player, juniorId, ct).ConfigureAwait(false);
        return new V113FamilyHandleResult(result.Status, [V113StatsPackets.EnableActions()]);
    }

    public async Task<V113FamilyHandleResult> HandleDeleteSeniorAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        _families.Register(player);
        var result = await _families.DeleteSeniorAsync(player, ct).ConfigureAwait(false);
        return new V113FamilyHandleResult(result.Status, [V113StatsPackets.EnableActions()]);
    }

    public async Task<V113FamilyHandleResult> HandleAcceptFamilyAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        int inviterId;
        string inviterName;
        bool accepted;
        try
        {
            inviterId = reader.ReadInt();
            inviterName = reader.ReadMapleString();
            accepted = reader.ReadByte() > 0;
        }
        catch (InvalidDataException)
        {
            return Empty(FamilyCommandStatus.InvalidOperation);
        }

        _families.Register(player);
        var inviter = await _sessions.FindOnlinePlayerByIdAsync(inviterId, ct).ConfigureAwait(false);
        if (inviter is not null)
        {
            _families.Register(inviter);
        }

        if (!accepted)
        {
            var denied = _families.DenyInvite(inviterId, player);
            if (denied.Succeeded)
            {
                await _sessions.SendPacketAsync(inviterId, V113FamilyPackets.FamilyJoinResponse(false, player.Character.Name), ct).ConfigureAwait(false);
            }

            return Empty(denied.Status);
        }

        if (inviter is null || !string.Equals(inviter.Character.Name, inviterName, StringComparison.OrdinalIgnoreCase))
        {
            return Empty(FamilyCommandStatus.TargetNotFound);
        }

        var result = await _families.AcceptInviteAsync(inviterId, player, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Empty(result.Status);
        }

        await _sessions.SendPacketAsync(inviterId, V113FamilyPackets.FamilyJoinResponse(true, player.Character.Name), ct).ConfigureAwait(false);
        return new V113FamilyHandleResult(
            FamilyCommandStatus.Success,
            [
                V113FamilyPackets.SeniorMessage(inviter.Character.Name),
                V113FamilyPackets.FamilyInfo(_families.GetFamilyInfo(player.Character.Id)),
            ]);
    }

    public async Task<V113FamilyHandleResult> HandleUseFamilyAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        int type;
        string? targetName = null;
        try
        {
            type = reader.ReadInt();
            if (type is 0 or 1)
            {
                targetName = reader.ReadMapleString();
            }
        }
        catch (InvalidDataException)
        {
            return Empty(FamilyCommandStatus.InvalidOperation);
        }

        _families.Register(player);
        Player? target = null;
        if (targetName is not null)
        {
            target = await _sessions.FindOnlinePlayerByNameAsync(targetName, ct).ConfigureAwait(false);
            if (target is not null)
            {
                _families.Register(target);
            }
        }

        var result = await _families.UseFamilyBuffAsync(player, type, target, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Empty(result.Status);
        }

        if (result.UpdateKind == FamilyUpdateKind.SummonRequested && target is not null)
        {
            await _sessions.SendPacketAsync(
                target.Character.Id,
                V113FamilyPackets.FamilySummonRequest(player.Character.Name, player.Character.MapId.ToString()),
                ct).ConfigureAwait(false);
            return Empty(FamilyCommandStatus.Success);
        }

        var packets = new List<byte[]>();
        if (result.Buff is { RepCost: > 0 } buff)
        {
            packets.Add(V113FamilyPackets.ChangeRep(-buff.RepCost));
            packets.Add(V113FamilyPackets.FamilySetPrivilege(buff));
        }

        return new V113FamilyHandleResult(FamilyCommandStatus.Success, packets, result.Warp);
    }

    public async Task<V113FamilyHandleResult> HandleFamilyPreceptAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        string notice;
        try
        {
            notice = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return Empty(FamilyCommandStatus.InvalidOperation);
        }

        _families.Register(player);
        var result = await _families.SetFamilyPreceptAsync(player, notice, ct).ConfigureAwait(false);
        return result.Succeeded
            ? SelfOnly(FamilyCommandStatus.Success, V113FamilyPackets.FamilyInfo(_families.GetFamilyInfo(player.Character.Id)))
            : Empty(result.Status);
    }

    public async Task<V113FamilyHandleResult> HandleFamilySummonAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        string summonerName;
        bool accepted;
        try
        {
            summonerName = reader.ReadMapleString();
            accepted = reader.ReadByte() > 0;
        }
        catch (InvalidDataException)
        {
            return Empty(FamilyCommandStatus.InvalidOperation);
        }

        _families.Register(player);
        var result = await _families.HandleFamilySummonAsync(player, accepted, summonerName, ct).ConfigureAwait(false);
        if (result.Succeeded && result.UpdateKind == FamilyUpdateKind.SummonAccepted && result.Buff is not null)
        {
            var summoner = await _sessions.FindOnlinePlayerByNameAsync(summonerName, ct).ConfigureAwait(false);
            if (summoner is not null)
            {
                await _sessions.SendPacketAsync(summoner.Character.Id, V113FamilyPackets.ChangeRep(-result.Buff.RepCost), ct).ConfigureAwait(false);
            }
        }

        return new V113FamilyHandleResult(result.Status, Array.Empty<byte[]>(), result.Warp);
    }

    private static V113FamilyHandleResult Empty(FamilyCommandStatus status) =>
        new(status, Array.Empty<byte[]>());

    private static V113FamilyHandleResult SelfOnly(FamilyCommandStatus status, params byte[][] packets) =>
        new(status, packets);

    private static V113FamilyHandleResult EnableActions(FamilyCommandStatus status) =>
        new(status, [V113StatsPackets.EnableActions()]);
}
