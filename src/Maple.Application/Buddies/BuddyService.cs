using Maple.Core.Characters;

namespace Maple.Application.Buddies;

public enum BuddyModifyKind
{
    Refresh,
    Add,
    Accept,
    Delete,
    Unknown,
}

public sealed record BuddyModifyRequest(
    BuddyModifyKind Kind,
    string BuddyName = "",
    string Group = "",
    int BuddyCharacterId = 0);

public sealed record BuddySelfResponse(
    byte? Message,
    IReadOnlyList<BuddyEntry>? BuddyList,
    BuddyEntry? PendingRequest);

public sealed record BuddyRemoteRequest(
    BuddyOnlinePlayer Target,
    int CharacterIdFrom,
    string NameFrom);

public sealed record BuddyRemoteChannelUpdate(
    BuddyOnlinePlayer Target,
    int CharacterId,
    int ChannelForClient);

public sealed record BuddyServiceResult(
    BuddySelfResponse Self,
    IReadOnlyList<BuddyRemoteRequest> RemoteRequests,
    IReadOnlyList<BuddyRemoteChannelUpdate> RemoteChannelUpdates)
{
    public static BuddyServiceResult Empty { get; } = new(
        new BuddySelfResponse(null, null, null),
        Array.Empty<BuddyRemoteRequest>(),
        Array.Empty<BuddyRemoteChannelUpdate>());
}

public sealed class BuddyService
{
    public const byte MessageOwnListFullOrAlreadyAdded = 11;
    public const byte MessageTargetListFull = 12;
    public const byte MessageTargetNotFound = 15;

    private readonly ICharacterRepository _characters;
    private readonly IBuddyOnlineRegistry _online;

    public BuddyService(ICharacterRepository characters, IBuddyOnlineRegistry online)
    {
        _characters = characters;
        _online = online;
    }

    public BuddyServiceResult LogOn(
        Character character,
        int channel,
        Func<byte[], CancellationToken, Task> sendPacket)
    {
        character.BuddyList.ResetRuntimeState();
        _online.Register(new BuddyOnlinePlayer(character.Id, character.Name, channel, character, sendPacket));
        RefreshBuddyChannels(character);

        return new BuddyServiceResult(
            Self(character, message: null, includeList: true, includePending: true),
            Array.Empty<BuddyRemoteRequest>(),
            PresenceUpdates(character, channel, online: true));
    }

    public BuddyServiceResult LogOff(Character character)
    {
        var current = _online.FindById(character.Id);
        var updates = PresenceUpdates(character, current?.Channel ?? -1, online: false);
        _online.Deregister(character.Id);
        character.BuddyList.ResetRuntimeState();

        return new BuddyServiceResult(
            new BuddySelfResponse(null, null, null),
            Array.Empty<BuddyRemoteRequest>(),
            updates);
    }

    public async Task<BuddyServiceResult> ModifyAsync(
        Character character,
        BuddyModifyRequest request,
        int channel,
        CancellationToken ct = default)
    {
        RefreshBuddyChannels(character);

        return request.Kind switch
        {
            BuddyModifyKind.Refresh => new BuddyServiceResult(
                Self(character, message: null, includeList: true, includePending: false),
                Array.Empty<BuddyRemoteRequest>(),
                Array.Empty<BuddyRemoteChannelUpdate>()),
            BuddyModifyKind.Add => await AddAsync(character, request.BuddyName, request.Group, channel, ct),
            BuddyModifyKind.Accept => await AcceptAsync(character, request.BuddyCharacterId, channel, ct),
            BuddyModifyKind.Delete => Delete(character, request.BuddyCharacterId),
            _ => BuddyServiceResult.Empty,
        };
    }

    private async Task<BuddyServiceResult> AddAsync(
        Character character,
        string buddyName,
        string group,
        int channel,
        CancellationToken ct)
    {
        if (buddyName.Length > 13 || group.Length > 16)
        {
            return Result(character, message: null, includeList: false, includePending: true);
        }

        var existing = character.BuddyList.Get(buddyName);
        if (existing is not null && existing.Group == group)
        {
            return Result(character, MessageOwnListFullOrAlreadyAdded, includeList: false, includePending: true);
        }

        if (existing is not null)
        {
            existing.Group = group;
            return Result(character, message: null, includeList: true, includePending: true);
        }

        if (character.BuddyList.IsFull())
        {
            return Result(character, MessageOwnListFullOrAlreadyAdded, includeList: false, includePending: false);
        }

        var onlineBuddy = _online.FindByName(buddyName);
        var buddyCharacter = onlineBuddy?.Character ?? await _characters.FindByNameAsync(buddyName, ct);
        if (buddyCharacter is null || buddyCharacter.Id == character.Id)
        {
            return Result(character, MessageTargetNotFound, includeList: false, includePending: true);
        }

        var remoteRequests = new List<BuddyRemoteRequest>();
        var remoteUpdates = new List<BuddyRemoteChannelUpdate>();

        var remoteEntry = buddyCharacter.BuddyList.Get(character.Id);
        var alreadyVisibleToTarget = remoteEntry?.Visible == true;
        if (remoteEntry is null && buddyCharacter.BuddyList.IsFull())
        {
            return Result(character, MessageTargetListFull, includeList: false, includePending: false);
        }

        if (remoteEntry is null)
        {
            remoteEntry = new BuddyEntry
            {
                CharacterId = character.Id,
                Name = character.Name,
                Group = BuddyList.DefaultGroup,
                Channel = channel,
                Visible = false,
                PendingRequest = true,
                RequestPrompted = onlineBuddy is not null,
            };
            buddyCharacter.BuddyList.Put(remoteEntry);

            if (onlineBuddy is not null)
            {
                remoteRequests.Add(new BuddyRemoteRequest(onlineBuddy, character.Id, character.Name));
            }
            else
            {
                await _characters.UpdateAsync(buddyCharacter, ct);
            }
        }
        else if (alreadyVisibleToTarget && onlineBuddy is not null)
        {
            remoteEntry.Channel = channel;
            remoteUpdates.Add(new BuddyRemoteChannelUpdate(onlineBuddy, character.Id, channel - 1));
        }

        var selfEntry = new BuddyEntry
        {
            CharacterId = buddyCharacter.Id,
            Name = buddyCharacter.Name,
            Group = string.IsNullOrEmpty(group) ? BuddyList.DefaultGroup : group,
            Channel = onlineBuddy?.Channel ?? -1,
            Visible = alreadyVisibleToTarget,
            PendingRequest = false,
        };
        character.BuddyList.Put(selfEntry);

        return new BuddyServiceResult(
            Self(character, message: null, includeList: true, includePending: true),
            remoteRequests,
            remoteUpdates);
    }

    private async Task<BuddyServiceResult> AcceptAsync(Character character, int buddyCharacterId, int channel, CancellationToken ct)
    {
        if (character.BuddyList.IsFull())
        {
            return Result(character, MessageOwnListFullOrAlreadyAdded, includeList: false, includePending: true);
        }

        var onlineBuddy = _online.FindById(buddyCharacterId);
        var buddyCharacter = onlineBuddy?.Character ?? await _characters.FindByIdAsync(buddyCharacterId, ct);
        if (buddyCharacter is null || buddyCharacter.Id == character.Id)
        {
            return Result(character, MessageOwnListFullOrAlreadyAdded, includeList: false, includePending: true);
        }

        var group = character.BuddyList.Get(buddyCharacterId)?.Group ?? BuddyList.DefaultGroup;
        character.BuddyList.Put(new BuddyEntry
        {
            CharacterId = buddyCharacter.Id,
            Name = buddyCharacter.Name,
            Group = string.IsNullOrEmpty(group) ? BuddyList.DefaultGroup : group,
            Channel = onlineBuddy?.Channel ?? -1,
            Visible = true,
            PendingRequest = false,
        });

        var remoteUpdates = new List<BuddyRemoteChannelUpdate>();
        var remoteEntry = buddyCharacter.BuddyList.Get(character.Id);
        if (remoteEntry is not null)
        {
            remoteEntry.Group = group;
            remoteEntry.Visible = true;
            remoteEntry.PendingRequest = false;
            remoteEntry.Channel = channel;

            if (onlineBuddy is not null)
            {
                remoteUpdates.Add(new BuddyRemoteChannelUpdate(onlineBuddy, character.Id, channel - 1));
            }
            else
            {
                await _characters.UpdateAsync(buddyCharacter, ct);
            }
        }

        return new BuddyServiceResult(
            Self(character, message: null, includeList: true, includePending: true),
            Array.Empty<BuddyRemoteRequest>(),
            remoteUpdates);
    }

    private BuddyServiceResult Delete(Character character, int buddyCharacterId)
    {
        var entry = character.BuddyList.Get(buddyCharacterId);
        var remoteUpdates = new List<BuddyRemoteChannelUpdate>();
        if (entry is { Visible: true })
        {
            var onlineBuddy = _online.FindById(buddyCharacterId);
            var remoteEntry = onlineBuddy?.Character.BuddyList.Get(character.Id);
            if (onlineBuddy is not null && remoteEntry is not null)
            {
                remoteEntry.Channel = -1;
                remoteUpdates.Add(new BuddyRemoteChannelUpdate(onlineBuddy, character.Id, -1));
            }
        }

        character.BuddyList.Remove(buddyCharacterId);
        return new BuddyServiceResult(
            Self(character, message: null, includeList: true, includePending: true),
            Array.Empty<BuddyRemoteRequest>(),
            remoteUpdates);
    }

    private BuddyServiceResult Result(Character character, byte? message, bool includeList, bool includePending)
    {
        return new BuddyServiceResult(
            Self(character, message, includeList, includePending),
            Array.Empty<BuddyRemoteRequest>(),
            Array.Empty<BuddyRemoteChannelUpdate>());
    }

    private BuddySelfResponse Self(Character character, byte? message, bool includeList, bool includePending)
    {
        RefreshBuddyChannels(character);
        var list = includeList ? Snapshot(character.BuddyList.Entries) : null;
        var pending = includePending ? character.BuddyList.TakeNextPendingRequest()?.Clone() : null;
        return new BuddySelfResponse(message, list, pending);
    }

    private void RefreshBuddyChannels(Character character)
    {
        foreach (var entry in character.BuddyList.Entries)
        {
            if (!entry.Visible)
            {
                entry.Channel = -1;
                continue;
            }

            entry.Channel = _online.FindById(entry.CharacterId)?.Channel ?? -1;
        }
    }

    private IReadOnlyList<BuddyRemoteChannelUpdate> PresenceUpdates(Character character, int channel, bool online)
    {
        var updates = new List<BuddyRemoteChannelUpdate>();
        foreach (var buddyId in character.BuddyList.GetBuddyIds())
        {
            var target = _online.FindById(buddyId);
            var targetEntry = target?.Character.BuddyList.Get(character.Id);
            if (target is null || targetEntry?.Visible != true)
            {
                continue;
            }

            targetEntry.Channel = online ? channel : -1;
            updates.Add(new BuddyRemoteChannelUpdate(target, character.Id, online ? channel - 1 : -1));
        }

        return updates;
    }

    private static IReadOnlyList<BuddyEntry> Snapshot(IEnumerable<BuddyEntry> entries)
        => entries.Select(static e => e.Clone()).ToList();
}
