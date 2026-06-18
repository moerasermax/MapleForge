using Maple.Core.Social;

namespace Maple.Application.Social;

public sealed class MessengerService
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Messenger> _messengers = new();
    private readonly Dictionary<int, int> _messengerByCharacter = new();
    private int _nextMessengerId;

    public MessengerService(int firstMessengerId = 1)
    {
        if (firstMessengerId <= 0) throw new ArgumentOutOfRangeException(nameof(firstMessengerId));
        _nextMessengerId = firstMessengerId;
    }

    public Messenger CreateMessenger(MessengerMember member)
    {
        lock (_gate)
        {
            if (_messengerByCharacter.TryGetValue(member.CharacterId, out var currentId) &&
                _messengers.TryGetValue(currentId, out var current))
            {
                return current.Snapshot();
            }

            var messenger = new Messenger(_nextMessengerId++, member);
            _messengers.Add(messenger.Id, messenger);
            _messengerByCharacter[member.CharacterId] = messenger.Id;
            return messenger.Snapshot();
        }
    }

    public bool JoinMessenger(int messengerId, MessengerMember member)
    {
        lock (_gate)
        {
            if (_messengerByCharacter.ContainsKey(member.CharacterId))
            {
                return false;
            }

            if (!_messengers.TryGetValue(messengerId, out var messenger))
            {
                return false;
            }

            if (!messenger.TryAddMember(member))
            {
                return false;
            }

            _messengerByCharacter[member.CharacterId] = messenger.Id;
            return true;
        }
    }

    public bool LeaveMessenger(int messengerId, int characterId)
    {
        lock (_gate)
        {
            if (!_messengers.TryGetValue(messengerId, out var messenger))
            {
                return false;
            }

            if (!messenger.TryRemoveMember(characterId, out _))
            {
                return false;
            }

            _messengerByCharacter.Remove(characterId);
            if (messenger.IsEmpty)
            {
                _messengers.Remove(messenger.Id);
            }

            return true;
        }
    }

    public Messenger? GetMessenger(int id)
    {
        lock (_gate)
        {
            return _messengers.TryGetValue(id, out var messenger) ? messenger.Snapshot() : null;
        }
    }

    public Messenger? GetMessengerForCharacter(int characterId)
    {
        lock (_gate)
        {
            if (!_messengerByCharacter.TryGetValue(characterId, out var messengerId))
            {
                return null;
            }

            return _messengers.TryGetValue(messengerId, out var messenger) ? messenger.Snapshot() : null;
        }
    }

    public bool IsCharacterInMessenger(int characterId)
    {
        lock (_gate)
        {
            return _messengerByCharacter.ContainsKey(characterId);
        }
    }
}
