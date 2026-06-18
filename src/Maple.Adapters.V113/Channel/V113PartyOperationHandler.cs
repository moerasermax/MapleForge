using Maple.Application.Parties;
using Maple.Core.IO;
using Maple.Core.Parties;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public sealed record V113PartySessionPlayer(
    int CharacterId,
    string Name,
    int Level,
    int JobId,
    int MapId,
    int ChannelIndex)
{
    public PartyMember ToPartyMember() =>
        new(CharacterId, Name, Level, JobId, MapId, ChannelIndex);
}

public interface IV113PartySessionHook
{
    ValueTask<V113PartySessionPlayer?> FindOnlinePlayerByNameAsync(string characterName, CancellationToken ct);

    Task SendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct);
}

public sealed class V113PartyOperationHandler
{
    private readonly PartyService _parties;
    private readonly IV113PartySessionHook _sessions;

    public V113PartyOperationHandler(PartyService parties, IV113PartySessionHook sessions)
    {
        _parties = parties;
        _sessions = sessions;
    }

    public async Task HandlePartyOperationAsync(
        PacketReader reader,
        Player player,
        int channelIndex,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(sendSelf);

        V113PartyClientOperation operation;
        try
        {
            operation = V113PartyPackets.ReadOperation(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        switch (operation)
        {
            case V113PartyClientOperation.Create:
                await HandleCreateAsync(player, channelIndex, sendSelf, ct);
                break;

            case V113PartyClientOperation.Leave:
                await HandleLeaveAsync(player, channelIndex, sendSelf, ct);
                break;

            case V113PartyClientOperation.Join:
                await HandleJoinAsync(reader, player, channelIndex, sendSelf, ct);
                break;

            case V113PartyClientOperation.Invite:
                await HandleInviteAsync(reader, player, sendSelf, ct);
                break;

            case V113PartyClientOperation.Expel:
                await HandleExpelAsync(reader, player, channelIndex, sendSelf, ct);
                break;

            case V113PartyClientOperation.ChangeLeader:
                await HandleChangeLeaderAsync(reader, player, channelIndex, sendSelf, ct);
                break;
        }
    }

    public async Task HandleDenyPartyRequestAsync(
        PacketReader reader,
        Player player,
        int channelIndex,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(sendSelf);

        byte action;
        int partyId;
        try
        {
            action = reader.ReadByte();
            partyId = reader.ReadInt();
        }
        catch (InvalidDataException)
        {
            return;
        }

        if (_parties.GetPartyForCharacter(player.Character.Id) is not null)
        {
            return;
        }

        var party = _parties.GetParty(partyId);
        if (party is null)
        {
            return;
        }

        if (action == 0x1B)
        {
            if (party.Members.Count >= 6)
            {
                await sendSelf(V113PartyPackets.PartyStatusMessage(V113PartyPackets.StatusPartyFull), ct);
                return;
            }

            var member = PartyMember.FromCharacter(player.Character, channelIndex);
            var result = _parties.JoinParty(partyId, member);
            if (result.Succeeded)
            {
                await BroadcastPartyUpdateAsync(result, player, channelIndex, sendSelf, ct);
            }
            else
            {
                await sendSelf(V113PartyPackets.PartyStatusMessage(ToPartyStatusMessage(result.Status)), ct);
            }
        }
        else
        {
            // Decline: notify the party leader
            try
            {
                await _sessions.SendToCharacterAsync(
                    party.LeaderId,
                    V113PartyPackets.PartyStatusMessage(23, player.Character.Name),
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* best effort */ }
        }
    }

    private async Task HandleCreateAsync(
        Player player,
        int channelIndex,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        if (IsBeginnerJob(player.Character.Job))
        {
            await sendSelf(V113PartyPackets.PartyStatusMessage(V113PartyPackets.StatusBeginnerCannotCreate), ct);
            return;
        }

        var leader = PartyMember.FromCharacter(player.Character, channelIndex);
        var result = _parties.CreateParty(leader);
        if (result.Succeeded && result.Party is not null)
        {
            await sendSelf(V113PartyPackets.PartyCreated(result.Party.Id), ct);
            return;
        }

        var currentParty = _parties.GetPartyForCharacter(player.Character.Id);
        if (currentParty is { LeaderId: var leaderId, Members.Count: 1 } && leaderId == player.Character.Id)
        {
            await sendSelf(V113PartyPackets.PartyCreated(currentParty.Id), ct);
            return;
        }

        await sendSelf(V113PartyPackets.PartyStatusMessage(ToPartyStatusMessage(result.Status)), ct);
    }

    private async Task HandleJoinAsync(
        PacketReader reader,
        Player player,
        int channelIndex,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        int partyId;
        try
        {
            partyId = reader.ReadInt();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var member = PartyMember.FromCharacter(player.Character, channelIndex);
        var result = _parties.JoinParty(partyId, member);
        if (result.Succeeded)
        {
            await BroadcastPartyUpdateAsync(result, player, channelIndex, sendSelf, ct);
            return;
        }

        await sendSelf(V113PartyPackets.PartyStatusMessage(ToPartyStatusMessage(result.Status)), ct);
    }

    private async Task HandleLeaveAsync(
        Player player,
        int channelIndex,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        var result = _parties.LeaveParty(player.Character.Id);
        if (result.Succeeded)
        {
            await BroadcastPartyUpdateAsync(result, player, channelIndex, sendSelf, ct);
        }
    }

    private async Task HandleInviteAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        string targetName;
        try
        {
            targetName = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var invitee = await _sessions.FindOnlinePlayerByNameAsync(targetName, ct);
        if (invitee is null)
        {
            await sendSelf(V113PartyPackets.PartyStatusMessage(V113PartyPackets.StatusCannotFindCharacter), ct);
            return;
        }

        if (!CanReceiveInvite(invitee))
        {
            return;
        }

        var result = _parties.InviteMember(player.Character.Id, invitee.ToPartyMember());
        if (!result.Succeeded || result.Party is null)
        {
            await sendSelf(V113PartyPackets.PartyStatusMessage(ToPartyStatusMessage(result.Status)), ct);
            return;
        }

        await _sessions.SendToCharacterAsync(
            invitee.CharacterId,
            V113PartyPackets.PartyInvite(result.Party.Id, player.Character.Name),
            ct);
    }

    private async Task HandleExpelAsync(
        PacketReader reader,
        Player player,
        int channelIndex,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        int targetId;
        try
        {
            targetId = reader.ReadInt();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = _parties.ExpelMember(player.Character.Id, targetId);
        if (result.Succeeded)
        {
            await BroadcastPartyUpdateAsync(result, player, channelIndex, sendSelf, ct);
            return;
        }

        await sendSelf(V113PartyPackets.PartyStatusMessage(ToPartyStatusMessage(result.Status)), ct);
    }

    private async Task HandleChangeLeaderAsync(
        PacketReader reader,
        Player player,
        int channelIndex,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        int newLeaderId;
        try
        {
            newLeaderId = reader.ReadInt();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = _parties.ChangeLeader(player.Character.Id, newLeaderId);
        if (result.Succeeded)
        {
            await BroadcastPartyUpdateAsync(result, player, channelIndex, sendSelf, ct);
            return;
        }

        await sendSelf(V113PartyPackets.PartyStatusMessage(ToPartyStatusMessage(result.Status)), ct);
    }

    private async Task BroadcastPartyUpdateAsync(
        PartyCommandResult result,
        Player currentPlayer,
        int currentChannelIndex,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        if (result.Party is null || result.Target is null || result.UpdateKind is null)
        {
            return;
        }

        foreach (var recipientId in result.Recipients)
        {
            var recipientChannel = ResolveRecipientChannel(result, recipientId, currentPlayer.Character.Id, currentChannelIndex);
            var packet = V113PartyPackets.UpdateParty(recipientChannel, result.Party, result.UpdateKind.Value, result.Target);

            if (recipientId == currentPlayer.Character.Id)
            {
                await sendSelf(packet, ct);
                continue;
            }

            try
            {
                await _sessions.SendToCharacterAsync(recipientId, packet, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Party broadcasts are best effort; stale sessions are cleaned by the central hook.
            }
        }
    }

    private static int ResolveRecipientChannel(
        PartyCommandResult result,
        int recipientId,
        int currentCharacterId,
        int currentChannelIndex)
    {
        if (recipientId == currentCharacterId)
        {
            return currentChannelIndex;
        }

        var member = result.Party?.GetMember(recipientId);
        if (member is not null)
        {
            return member.ChannelIndex;
        }

        return result.Target?.CharacterId == recipientId
            ? result.Target.ChannelIndex
            : currentChannelIndex;
    }

    private static byte ToPartyStatusMessage(PartyCommandStatus status) => status switch
    {
        PartyCommandStatus.PartyFull => V113PartyPackets.StatusPartyFull,
        PartyCommandStatus.AlreadyInParty or PartyCommandStatus.TargetAlreadyInParty => V113PartyPackets.StatusAlreadyInParty,
        PartyCommandStatus.NotInParty => V113PartyPackets.StatusNotInParty,
        PartyCommandStatus.PartyNotFound or PartyCommandStatus.TargetNotFound => V113PartyPackets.StatusCannotFindCharacter,
        _ => V113PartyPackets.StatusUnknownError,
    };

    private static bool IsBeginnerJob(short jobId) => jobId is 0 or 1000 or 2000;

    private static bool CanReceiveInvite(V113PartySessionPlayer player) =>
        player.Level > 10 || player.JobId == 200;
}
