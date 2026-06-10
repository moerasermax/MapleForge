using Maple.Application.OnlinePlayers;
using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Application.Fame;

public enum FameResultStatus
{
    Success,
    TargetNotFound,
    Self,
    UnderLevel,
    AlreadyToday,
    AlreadyThisMonth,
    OutOfRange,
    InvalidMode,
}

public sealed record FameResult(
    FameResultStatus Status,
    byte Mode,
    OnlinePlayer? Target = null,
    short NewFame = 0);

public sealed class FameService
{
    private const long OneDayMilliseconds = 24L * 60L * 60L * 1000L;
    private const long ThirtyDaysMilliseconds = 30L * OneDayMilliseconds;
    private const short FameLimit = 30000;

    private readonly IOnlinePlayerRegistry _onlinePlayers;

    public FameService(IOnlinePlayerRegistry onlinePlayers)
    {
        _onlinePlayers = onlinePlayers;
    }

    public FameResult GiveFame(Player giver, int targetCharacterId, byte mode, long nowUnixMilliseconds)
    {
        if (mode is not 0 and not 1)
        {
            return new FameResult(FameResultStatus.InvalidMode, mode);
        }

        var target = _onlinePlayers.FindById(targetCharacterId);
        if (target is null || target.Character.MapId != giver.Character.MapId)
        {
            return new FameResult(FameResultStatus.TargetNotFound, mode);
        }

        if (target.CharacterId == giver.Character.Id)
        {
            return new FameResult(FameResultStatus.Self, mode, target, target.Character.Fame);
        }

        if (giver.Character.Level < 15)
        {
            return new FameResult(FameResultStatus.UnderLevel, mode, target, target.Character.Fame);
        }

        PruneOldFameHistory(giver.Character, nowUnixMilliseconds);

        if (giver.Character.LastFameAtUnixMillis >= nowUnixMilliseconds - OneDayMilliseconds)
        {
            return new FameResult(FameResultStatus.AlreadyToday, mode, target, target.Character.Fame);
        }

        if (giver.Character.FameHistory.Any(r => r.TargetCharacterId == target.CharacterId))
        {
            return new FameResult(FameResultStatus.AlreadyThisMonth, mode, target, target.Character.Fame);
        }

        var delta = mode == 0 ? -1 : 1;
        var nextFame = target.Character.Fame + delta;
        if (nextFame is > FameLimit or < -FameLimit)
        {
            return new FameResult(FameResultStatus.OutOfRange, mode, target, target.Character.Fame);
        }

        target.Character.Fame = (short)nextFame;
        giver.Character.LastFameAtUnixMillis = nowUnixMilliseconds;
        giver.Character.FameHistory.Add(new FameRecord
        {
            TargetCharacterId = target.CharacterId,
            GivenAtUnixMillis = nowUnixMilliseconds,
        });

        return new FameResult(FameResultStatus.Success, mode, target, target.Character.Fame);
    }

    private static void PruneOldFameHistory(Character character, long nowUnixMilliseconds)
    {
        var cutoff = nowUnixMilliseconds - ThirtyDaysMilliseconds;
        character.FameHistory.RemoveAll(r => r.GivenAtUnixMillis < cutoff);
    }
}
