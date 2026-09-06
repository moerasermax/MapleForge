using Maple.Application.Social;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.Social;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public sealed record V113MessengerSessionPlayer(
    int CharacterId,
    string Name,
    int ChannelIndex,
    Character Character)
{
    public MessengerMember ToMessengerMember(int position = 0) =>
        new(CharacterId, Name, ChannelIndex, position);
}

public interface IV113MessengerSessionHook
{
    ValueTask<V113MessengerSessionPlayer?> FindOnlinePlayerByNameAsync(string characterName, CancellationToken ct);

    Task SendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct);
}

public sealed class V113MessengerHandler
{
    private readonly MessengerService _messengers;
    private readonly IV113MessengerSessionHook _sessions;

    public V113MessengerHandler(MessengerService messengers, IV113MessengerSessionHook sessions)
    {
        _messengers = messengers;
        _sessions = sessions;
    }

    public async Task HandleMessengerAsync(
        PacketReader reader,
        Player player,
        int channelIndex,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(sendSelf);

        V113MessengerClientMode mode;
        try
        {
            mode = V113MessengerPackets.ReadMode(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        switch (mode)
        {
            case V113MessengerClientMode.Open:
                await HandleOpenAsync(reader, player, channelIndex, sendSelf, ct);
                break;

            case V113MessengerClientMode.Exit:
                await HandleExitAsync(player, ct);
                break;

            case V113MessengerClientMode.Invite:
                await HandleInviteAsync(reader, player, sendSelf, ct);
                break;

            case V113MessengerClientMode.Decline:
                await HandleDeclineAsync(reader, player, ct);
                break;

            case V113MessengerClientMode.Message:
                await HandleMessageAsync(reader, player, ct);
                break;
        }
    }

    private async Task HandleOpenAsync(
        PacketReader reader,
        Player player,
        int channelIndex,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        int messengerId;
        try
        {
            messengerId = reader.ReadInt();
        }
        catch (InvalidDataException)
        {
            return;
        }

        if (_messengers.GetMessengerForCharacter(player.Character.Id) is not null)
        {
            return;
        }

        if (messengerId == 0)
        {
            _messengers.CreateMessenger(ToMember(player, channelIndex));
            return;
        }

        if (!_messengers.JoinMessenger(messengerId, ToMember(player, channelIndex)))
        {
            return;
        }

        var messenger = _messengers.GetMessenger(messengerId);
        if (messenger is not null)
        {
            await BroadcastJoinAsync(messenger, player, channelIndex, sendSelf, ct);
        }
    }

    private Task HandleExitAsync(Player player, CancellationToken ct) => LeaveMessengerAndNotifyAsync(player, ct);

    /// <summary>
    /// 對照 Java <c>MapleClient</c> 斷線流程：<c>World.Messenger.leaveMessenger(messengerid, chrm)</c>
    /// 在玩家斷線（非主動退出）時也要觸發，否則其他成員的密友聊天視窗永遠留著一個已離線的殘影。
    /// 與主動 EXIT（<see cref="HandleExitAsync"/>）共用同一段離開+廣播邏輯。
    /// </summary>
    public Task NotifyDisconnectAsync(Player player, CancellationToken ct) => LeaveMessengerAndNotifyAsync(player, ct);

    private async Task LeaveMessengerAndNotifyAsync(Player player, CancellationToken ct)
    {
        var messenger = _messengers.GetMessengerForCharacter(player.Character.Id);
        var member = messenger?.GetMember(player.Character.Id);
        if (messenger is null || member is null)
        {
            return;
        }

        if (!_messengers.LeaveMessenger(messenger.Id, player.Character.Id))
        {
            return;
        }

        var packet = V113MessengerPackets.RemoveMessengerPlayer(member.Position);
        foreach (var recipient in messenger.Members.Where(m => m is not null && m.CharacterId != player.Character.Id))
        {
            await TrySendToCharacterAsync(recipient!.CharacterId, packet, ct);
        }
    }

    private async Task HandleInviteAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        var messenger = _messengers.GetMessengerForCharacter(player.Character.Id);
        if (messenger is null || messenger.GetLowestPosition() >= Messenger.MaxMembers)
        {
            return;
        }

        string targetName;
        try
        {
            targetName = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var target = await _sessions.FindOnlinePlayerByNameAsync(targetName, ct);
        if (target is null)
        {
            await sendSelf(V113MessengerPackets.MessengerNote(targetName, 4, 0), ct);
            return;
        }

        if (_messengers.IsCharacterInMessenger(target.CharacterId))
        {
            await sendSelf(
                V113MessengerPackets.MessengerChat($"{player.Character.Name} : {target.Name} is already using Maple Messenger"),
                ct);
            return;
        }

        await sendSelf(V113MessengerPackets.MessengerNote(target.Name, 4, 1), ct);
        await TrySendToCharacterAsync(
            target.CharacterId,
            V113MessengerPackets.MessengerInvite(player.Character.Name, messenger.Id),
            ct);
    }

    private async Task HandleDeclineAsync(PacketReader reader, Player player, CancellationToken ct)
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

        var target = await _sessions.FindOnlinePlayerByNameAsync(targetName, ct);
        if (target is null || !_messengers.IsCharacterInMessenger(target.CharacterId))
        {
            return;
        }

        await TrySendToCharacterAsync(
            target.CharacterId,
            V113MessengerPackets.MessengerNote(player.Character.Name, 5, 0),
            ct);
    }

    private async Task HandleMessageAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        var messenger = _messengers.GetMessengerForCharacter(player.Character.Id);
        if (messenger is null)
        {
            return;
        }

        string text;
        try
        {
            text = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var packet = V113MessengerPackets.MessengerChat(text);
        foreach (var recipient in messenger.Members.Where(m => m is not null && m.CharacterId != player.Character.Id))
        {
            await TrySendToCharacterAsync(recipient!.CharacterId, packet, ct);
        }
    }

    private async Task BroadcastJoinAsync(
        Messenger messenger,
        Player joinedPlayer,
        int channelIndex,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        var joinedMember = messenger.GetMember(joinedPlayer.Character.Id);
        if (joinedMember is null)
        {
            return;
        }

        foreach (var member in messenger.Members.Where(static m => m is not null).Select(static m => m!))
        {
            if (member.CharacterId == joinedPlayer.Character.Id)
            {
                await sendSelf(V113MessengerPackets.JoinMessenger(member.Position), ct);
                continue;
            }

            await TrySendToCharacterAsync(
                member.CharacterId,
                V113MessengerPackets.AddMessengerPlayer(
                    joinedPlayer.Character.Name,
                    joinedPlayer.Character,
                    joinedMember.Position,
                    channelIndex),
                ct);

            var existing = await _sessions.FindOnlinePlayerByNameAsync(member.Name, ct);
            var packet = existing is null
                ? V113MessengerPackets.AddMessengerPlayer(member.Name, member.Position, member.ChannelIndex)
                : V113MessengerPackets.AddMessengerPlayer(existing.Name, existing.Character, member.Position, existing.ChannelIndex);
            await sendSelf(packet, ct);
        }
    }

    private async Task TrySendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct)
    {
        try
        {
            await _sessions.SendToCharacterAsync(characterId, packet, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }
    }

    private static MessengerMember ToMember(Player player, int channelIndex) =>
        new(player.Character.Id, player.Character.Name, channelIndex, 0);
}
