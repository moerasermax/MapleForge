using Maple.Core.Parties;

namespace Maple.Application.Parties;

public enum PartyUpdateKind
{
    Join,
    Leave,
    Expel,
    Disband,
    SilentUpdate,
    LogOnOff,
    ChangeLeader,
    ChangeLeaderDisconnect,
}

public enum PartyCommandStatus
{
    Success,
    AlreadyInParty,
    NotInParty,
    PartyNotFound,
    PartyFull,
    NotLeader,
    TargetNotFound,
    TargetAlreadyInParty,
    InvalidOperation,
}

public sealed record PartyCommandResult(
    PartyCommandStatus Status,
    PartyState? Party = null,
    PartyMember? Target = null,
    PartyUpdateKind? UpdateKind = null,
    IReadOnlyList<int>? RecipientCharacterIds = null)
{
    public bool Succeeded => Status == PartyCommandStatus.Success;

    public IReadOnlyList<int> Recipients => RecipientCharacterIds ?? Array.Empty<int>();
}

public sealed record PartyInviteResult(
    PartyCommandStatus Status,
    PartyState? Party = null,
    PartyMember? Invitee = null)
{
    public bool Succeeded => Status == PartyCommandStatus.Success;
}

public interface IPartyRegistry
{
    PartyCommandResult CreateParty(PartyMember leader);

    PartyState? GetParty(int partyId);

    PartyState? GetPartyForCharacter(int characterId);

    bool IsCharacterInParty(int characterId);

    PartyCommandResult JoinParty(int partyId, PartyMember member);

    PartyCommandResult LeaveParty(int characterId);

    PartyCommandResult ExpelMember(int leaderId, int targetId);

    PartyCommandResult ChangeLeader(int leaderId, int newLeaderId, bool disconnected = false);

    PartyCommandResult UpdateMember(PartyMember member, PartyUpdateKind updateKind = PartyUpdateKind.SilentUpdate);
}

public sealed class InMemoryPartyRegistry : IPartyRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Party> _parties = new();
    private readonly Dictionary<int, int> _partyByCharacter = new();
    private int _nextPartyId;

    public InMemoryPartyRegistry(int firstPartyId = 1)
    {
        if (firstPartyId <= 0) throw new ArgumentOutOfRangeException(nameof(firstPartyId));
        _nextPartyId = firstPartyId;
    }

    public PartyCommandResult CreateParty(PartyMember leader)
    {
        lock (_gate)
        {
            if (_partyByCharacter.ContainsKey(leader.CharacterId))
            {
                return new PartyCommandResult(PartyCommandStatus.AlreadyInParty, GetPartyForCharacterLocked(leader.CharacterId));
            }

            var party = new Party(_nextPartyId++, leader);
            _parties.Add(party.Id, party);
            _partyByCharacter.Add(leader.CharacterId, party.Id);
            return new PartyCommandResult(PartyCommandStatus.Success, party.Snapshot());
        }
    }

    public PartyState? GetParty(int partyId)
    {
        lock (_gate)
        {
            return _parties.TryGetValue(partyId, out var party) ? party.Snapshot() : null;
        }
    }

    public PartyState? GetPartyForCharacter(int characterId)
    {
        lock (_gate)
        {
            return GetPartyForCharacterLocked(characterId);
        }
    }

    public bool IsCharacterInParty(int characterId)
    {
        lock (_gate)
        {
            return _partyByCharacter.ContainsKey(characterId);
        }
    }

    public PartyCommandResult JoinParty(int partyId, PartyMember member)
    {
        lock (_gate)
        {
            if (_partyByCharacter.ContainsKey(member.CharacterId))
            {
                return new PartyCommandResult(PartyCommandStatus.AlreadyInParty, GetPartyForCharacterLocked(member.CharacterId));
            }

            if (!_parties.TryGetValue(partyId, out var party))
            {
                return new PartyCommandResult(PartyCommandStatus.PartyNotFound);
            }

            if (party.IsFull)
            {
                return new PartyCommandResult(PartyCommandStatus.PartyFull, party.Snapshot());
            }

            if (!party.TryAddMember(member))
            {
                return new PartyCommandResult(PartyCommandStatus.InvalidOperation, party.Snapshot(), member);
            }

            _partyByCharacter.Add(member.CharacterId, party.Id);
            var snapshot = party.Snapshot();
            return new PartyCommandResult(
                PartyCommandStatus.Success,
                snapshot,
                member,
                PartyUpdateKind.Join,
                OnlineRecipientIds(snapshot));
        }
    }

    public PartyCommandResult LeaveParty(int characterId)
    {
        lock (_gate)
        {
            if (!TryGetPartyByCharacterLocked(characterId, out var party))
            {
                return new PartyCommandResult(PartyCommandStatus.NotInParty);
            }

            var target = party.GetMember(characterId);
            if (target is null)
            {
                return new PartyCommandResult(PartyCommandStatus.TargetNotFound, party.Snapshot());
            }

            if (party.LeaderId == characterId)
            {
                var disbanded = party.Snapshot();
                _parties.Remove(party.Id);
                foreach (var member in disbanded.Members)
                {
                    _partyByCharacter.Remove(member.CharacterId);
                }

                return new PartyCommandResult(
                    PartyCommandStatus.Success,
                    disbanded,
                    target,
                    PartyUpdateKind.Disband,
                    OnlineRecipientIds(disbanded));
            }

            party.TryRemoveMember(characterId, out _);
            _partyByCharacter.Remove(characterId);

            var snapshot = party.Snapshot();
            return new PartyCommandResult(
                PartyCommandStatus.Success,
                snapshot,
                target,
                PartyUpdateKind.Leave,
                OnlineRecipientIds(snapshot, target));
        }
    }

    public PartyCommandResult ExpelMember(int leaderId, int targetId)
    {
        lock (_gate)
        {
            if (!TryGetPartyByCharacterLocked(leaderId, out var party))
            {
                return new PartyCommandResult(PartyCommandStatus.NotInParty);
            }

            if (party.LeaderId != leaderId)
            {
                return new PartyCommandResult(PartyCommandStatus.NotLeader, party.Snapshot());
            }

            if (targetId == leaderId)
            {
                return new PartyCommandResult(PartyCommandStatus.InvalidOperation, party.Snapshot());
            }

            var target = party.GetMember(targetId);
            if (target is null)
            {
                return new PartyCommandResult(PartyCommandStatus.TargetNotFound, party.Snapshot());
            }

            party.TryRemoveMember(targetId, out _);
            _partyByCharacter.Remove(targetId);

            var snapshot = party.Snapshot();
            return new PartyCommandResult(
                PartyCommandStatus.Success,
                snapshot,
                target,
                PartyUpdateKind.Expel,
                OnlineRecipientIds(snapshot, target));
        }
    }

    public PartyCommandResult ChangeLeader(int leaderId, int newLeaderId, bool disconnected = false)
    {
        lock (_gate)
        {
            if (!TryGetPartyByCharacterLocked(leaderId, out var party))
            {
                return new PartyCommandResult(PartyCommandStatus.NotInParty);
            }

            if (party.LeaderId != leaderId)
            {
                return new PartyCommandResult(PartyCommandStatus.NotLeader, party.Snapshot());
            }

            var newLeader = party.GetMember(newLeaderId);
            if (newLeader is null)
            {
                return new PartyCommandResult(PartyCommandStatus.TargetNotFound, party.Snapshot());
            }

            party.TryChangeLeader(newLeaderId);

            var snapshot = party.Snapshot();
            return new PartyCommandResult(
                PartyCommandStatus.Success,
                snapshot,
                newLeader,
                disconnected ? PartyUpdateKind.ChangeLeaderDisconnect : PartyUpdateKind.ChangeLeader,
                OnlineRecipientIds(snapshot));
        }
    }

    public PartyCommandResult UpdateMember(PartyMember member, PartyUpdateKind updateKind = PartyUpdateKind.SilentUpdate)
    {
        lock (_gate)
        {
            if (!TryGetPartyByCharacterLocked(member.CharacterId, out var party))
            {
                return new PartyCommandResult(PartyCommandStatus.NotInParty);
            }

            if (!party.TryUpdateMember(member))
            {
                return new PartyCommandResult(PartyCommandStatus.TargetNotFound, party.Snapshot(), member);
            }

            var snapshot = party.Snapshot();
            return new PartyCommandResult(
                PartyCommandStatus.Success,
                snapshot,
                member,
                updateKind,
                OnlineRecipientIds(snapshot));
        }
    }

    private PartyState? GetPartyForCharacterLocked(int characterId)
    {
        if (!_partyByCharacter.TryGetValue(characterId, out var partyId))
        {
            return null;
        }

        return _parties.TryGetValue(partyId, out var party) ? party.Snapshot() : null;
    }

    private bool TryGetPartyByCharacterLocked(int characterId, out Party party)
    {
        party = null!;
        if (!_partyByCharacter.TryGetValue(characterId, out var partyId))
        {
            return false;
        }

        if (!_parties.TryGetValue(partyId, out var found))
        {
            return false;
        }

        party = found;
        return true;
    }

    private static IReadOnlyList<int> OnlineRecipientIds(PartyState party, PartyMember? extra = null)
    {
        var ids = party.Members
            .Where(static m => m.IsOnline)
            .Select(static m => m.CharacterId)
            .ToList();

        if (extra is { IsOnline: true } && !ids.Contains(extra.CharacterId))
        {
            ids.Add(extra.CharacterId);
        }

        return ids;
    }
}

public sealed class PartyService
{
    private readonly IPartyRegistry _registry;

    public PartyService(IPartyRegistry registry)
    {
        _registry = registry;
    }

    public PartyCommandResult CreateParty(PartyMember leader) =>
        _registry.CreateParty(leader);

    public PartyState? GetParty(int partyId) =>
        _registry.GetParty(partyId);

    public PartyState? GetPartyForCharacter(int characterId) =>
        _registry.GetPartyForCharacter(characterId);

    public bool IsCharacterInParty(int characterId) =>
        _registry.IsCharacterInParty(characterId);

    public PartyCommandResult JoinParty(int partyId, PartyMember member) =>
        _registry.JoinParty(partyId, member);

    public PartyCommandResult LeaveParty(int characterId) =>
        _registry.LeaveParty(characterId);

    public PartyCommandResult ExpelMember(int leaderId, int targetId) =>
        _registry.ExpelMember(leaderId, targetId);

    public PartyCommandResult ChangeLeader(int leaderId, int newLeaderId, bool disconnected = false) =>
        _registry.ChangeLeader(leaderId, newLeaderId, disconnected);

    public PartyCommandResult UpdateMember(PartyMember member, PartyUpdateKind updateKind = PartyUpdateKind.SilentUpdate) =>
        _registry.UpdateMember(member, updateKind);

    public PartyInviteResult InviteMember(int inviterId, PartyMember invitee)
    {
        var party = _registry.GetPartyForCharacter(inviterId);
        if (party is null)
        {
            return new PartyInviteResult(PartyCommandStatus.NotInParty);
        }

        if (_registry.IsCharacterInParty(invitee.CharacterId))
        {
            return new PartyInviteResult(PartyCommandStatus.TargetAlreadyInParty, party, invitee);
        }

        if (party.IsFull)
        {
            return new PartyInviteResult(PartyCommandStatus.PartyFull, party, invitee);
        }

        return new PartyInviteResult(PartyCommandStatus.Success, party, invitee);
    }
}
