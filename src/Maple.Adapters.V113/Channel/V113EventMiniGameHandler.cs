using Maple.Application.Events;
using Maple.Core.MiniGames;
using Maple.Core.World;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal sealed record V113EventMiniGameHandleResult(
    bool Handled,
    IReadOnlyList<byte[]> SelfPackets,
    IReadOnlyList<byte[]> MapPackets,
    bool CharacterMutated);

public sealed class V113EventMiniGameHandler
{
    private const byte RpsModeStart = 0;
    private const byte RpsModeAnswer = 1;
    private const byte RpsModeTimeout = 2;
    private const byte RpsModeContinue = 3;
    private const byte RpsModeLeave = 4;
    private const byte RpsModeRetry = 5;

    private const byte BeansAdjustPower = 0x00;
    private const byte BeansStart = 0x04;
    private const byte BeansPause = 0x06;
    private const byte BeansOpenPrizeHole = 0x08;
    private const byte BeansLight = 0x0B;
    private const byte BeansMarqueeReward = 0x0D;
    private const byte BeansShoot = 0x0E;
    private const byte BeansTiming = 0x0F;

    private readonly CoconutEventService _coconutEvents;
    private readonly Func<RpsChoice>? _rpsOpponentChoiceProvider;

    public V113EventMiniGameHandler(CoconutEventService coconutEvents)
        : this(coconutEvents, null)
    {
    }

    internal V113EventMiniGameHandler(
        CoconutEventService coconutEvents,
        Func<RpsChoice>? rpsOpponentChoiceProvider)
    {
        ArgumentNullException.ThrowIfNull(coconutEvents);
        _coconutEvents = coconutEvents;
        _rpsOpponentChoiceProvider = rpsOpponentChoiceProvider;
    }

    internal V113EventMiniGameHandleResult HandleRpsGame(PacketReader reader, Player player)
    {
        if (reader.Remaining < 1)
        {
            return SelfOnly(V113EventMiniGamePackets.RpsMode(13));
        }

        var mode = reader.ReadByte();
        return mode switch
        {
            RpsModeStart or RpsModeRetry => StartRps(player, mode),
            RpsModeAnswer => AnswerRps(reader, player),
            RpsModeTimeout => TimeoutRps(player),
            RpsModeContinue => ContinueRps(player),
            RpsModeLeave => LeaveRps(player),
            _ => SelfOnly(V113StatsPackets.EnableActions()),
        };
    }

    internal V113EventMiniGameHandleResult HandleCoconut(PacketReader reader, Player player)
    {
        if (reader.Remaining < 2)
        {
            return SelfOnly(V113StatsPackets.EnableActions());
        }

        var coconutId = reader.ReadShort();
        var result = _coconutEvents.Hit(player.Character.MapId, coconutId, player.CoconutTeam);
        if (!result.Applied)
        {
            return SelfOnly(V113StatsPackets.EnableActions());
        }

        var packets = new List<byte[]>
        {
            V113EventMiniGamePackets.HitCoconut(
                spawn: false,
                id: result.CoconutId,
                type: V113EventMiniGamePackets.CoconutPacketType(result.Outcome)),
        };

        if (result.ScoreChanged)
        {
            packets.Add(V113EventMiniGamePackets.CoconutScore(result.MapleScore, result.StoryScore));
        }

        return MapOnly(packets);
    }

    internal V113EventMiniGameHandleResult HandleBeansGameAction(PacketReader reader, Player player)
    {
        if (reader.Remaining < 1)
        {
            return SelfOnly(V113StatsPackets.EnableActions());
        }

        var subtype = reader.ReadByte();
        return subtype switch
        {
            BeansAdjustPower => SelfOnly(V113StatsPackets.EnableActions()),
            BeansStart or 0x01 => StartBeans(player),
            BeansPause or 0x02 => SelfOnly(
                V113EventMiniGamePackets.UpdateBeans(player.Character.Id, player.Character.Beans),
                V113StatsPackets.EnableActions()),
            BeansOpenPrizeHole or 0x03 => SelfOnly(V113StatsPackets.EnableActions()),
            BeansLight => LightBeans(player),
            BeansMarqueeReward or 0x05 => SelfOnly(V113StatsPackets.EnableActions()),
            BeansShoot => ShootBeans(reader, player),
            BeansTiming or 0x07 => SelfOnly(V113StatsPackets.EnableActions()),
            _ => SelfOnly(V113StatsPackets.EnableActions()),
        };
    }

    private V113EventMiniGameHandleResult StartRps(Player player, byte clientMode)
    {
        if (player.RpsSession is { IsActive: true } existing)
        {
            var reward = existing.CashOut();
            if (reward > 0)
            {
                player.GainMeso(reward);
            }
        }

        if (player.Character.Meso < RpsSession.EntryFee)
        {
            return SelfOnly(V113EventMiniGamePackets.RpsMode(6, mesos: player.Character.Meso));
        }

        player.GainMeso(-RpsSession.EntryFee);
        var session = new RpsSession(player.Character.Id, _rpsOpponentChoiceProvider);
        session.Start();
        player.SetRpsSession(session);

        return new V113EventMiniGameHandleResult(
            true,
            [V113EventMiniGamePackets.RpsMode((byte)(0x09 + clientMode))],
            Array.Empty<byte[]>(),
            CharacterMutated: true);
    }

    private static V113EventMiniGameHandleResult AnswerRps(PacketReader reader, Player player)
    {
        if (reader.Remaining < 1 || player.RpsSession is not { IsActive: true } session)
        {
            return SelfOnly(V113EventMiniGamePackets.RpsMode(13));
        }

        var rawChoice = reader.ReadByte();
        if (rawChoice > 2)
        {
            session.End();
            player.ClearRpsSession();
            return SelfOnly(V113EventMiniGamePackets.RpsMode(13));
        }

        RpsResult result;
        try
        {
            result = session.Play((RpsChoice)rawChoice);
        }
        catch (InvalidOperationException)
        {
            return SelfOnly(V113EventMiniGamePackets.RpsMode(13));
        }

        var answer = result switch
        {
            RpsResult.Win => session.Wins,
            RpsResult.Tie => session.Wins,
            _ => -1,
        };

        var packet = V113EventMiniGamePackets.RpsMode(
            11,
            selection: (int)(session.LastOpponentChoice ?? RpsChoice.Rock),
            answer: answer);

        if (result == RpsResult.Lose || !session.IsActive)
        {
            player.ClearRpsSession();
        }

        return SelfOnly(packet);
    }

    private static V113EventMiniGameHandleResult TimeoutRps(Player player)
    {
        if (player.RpsSession is not { IsActive: true } session)
        {
            return SelfOnly(V113EventMiniGamePackets.RpsMode(13));
        }

        session.End();
        player.ClearRpsSession();
        return SelfOnly(V113EventMiniGamePackets.RpsMode(10));
    }

    private static V113EventMiniGameHandleResult ContinueRps(Player player)
    {
        if (player.RpsSession is not { } session || !session.Continue())
        {
            return CashOutAndLeaveRps(player);
        }

        return SelfOnly(V113EventMiniGamePackets.RpsMode(12));
    }

    private static V113EventMiniGameHandleResult LeaveRps(Player player) => CashOutAndLeaveRps(player);

    private static V113EventMiniGameHandleResult CashOutAndLeaveRps(Player player)
    {
        var characterMutated = false;
        if (player.RpsSession is { } session)
        {
            var reward = session.CashOut();
            if (reward > 0)
            {
                player.GainMeso(reward);
                characterMutated = true;
            }

            player.ClearRpsSession();
        }

        return new V113EventMiniGameHandleResult(
            true,
            [V113EventMiniGamePackets.RpsMode(13)],
            Array.Empty<byte[]>(),
            characterMutated);
    }

    private static V113EventMiniGameHandleResult StartBeans(Player player)
    {
        var result = player.BeansGameSession.Start(player.Character.Beans);
        if (result.Status == BeansGameActionStatus.Success)
        {
            player.Character.Beans = result.BeansAfter;
            return new V113EventMiniGameHandleResult(
                true,
                [
                    V113EventMiniGamePackets.ShowBeans(player.Character.Beans),
                    V113EventMiniGamePackets.UpdateBeans(player.Character.Id, player.Character.Beans),
                    V113StatsPackets.EnableActions(),
                ],
                Array.Empty<byte[]>(),
                CharacterMutated: true);
        }

        return SelfOnly(
            V113EventMiniGamePackets.ExitBeans(),
            V113StatsPackets.EnableActions());
    }

    private static V113EventMiniGameHandleResult ShootBeans(PacketReader reader, Player player)
    {
        var count = 1;
        if (reader.Remaining >= 2)
        {
            _ = reader.ReadByte();
            count = reader.ReadByte();
        }
        else if (reader.Remaining == 1)
        {
            count = reader.ReadByte();
        }

        var result = player.BeansGameSession.Shoot(player.Character.Beans, count);
        if (result.Status == BeansGameActionStatus.Success)
        {
            player.Character.Beans = result.BeansAfter;
            return new V113EventMiniGameHandleResult(
                true,
                [
                    V113EventMiniGamePackets.UpdateBeans(player.Character.Id, player.Character.Beans),
                    V113StatsPackets.EnableActions(),
                ],
                Array.Empty<byte[]>(),
                CharacterMutated: true);
        }

        return SelfOnly(
            V113EventMiniGamePackets.ExitBeans(),
            V113StatsPackets.EnableActions());
    }

    private static V113EventMiniGameHandleResult LightBeans(Player player)
        => SelfOnly(
            V113EventMiniGamePackets.SetBeanLightLevel(player.BeansGameSession.AddLight()),
            V113StatsPackets.EnableActions());

    private static V113EventMiniGameHandleResult SelfOnly(params byte[][] packets)
        => new(true, packets, Array.Empty<byte[]>(), CharacterMutated: false);

    private static V113EventMiniGameHandleResult MapOnly(IReadOnlyList<byte[]> packets)
        => new(true, Array.Empty<byte[]>(), packets, CharacterMutated: false);
}
