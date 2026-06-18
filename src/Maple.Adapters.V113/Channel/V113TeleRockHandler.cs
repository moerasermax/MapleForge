using Maple.Application.Maps;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113UseTeleRockRequest(byte RockType, byte Mode, int MapId, string CharacterName)
{
    public bool IsMapMode => Mode == 0;
    public bool IsPlayerMode => Mode == 1;
}

internal sealed record V113UseTeleRockResult(
    bool Handled,
    bool Success,
    V113UseTeleRockRequest Request,
    int? WarpMapId,
    IReadOnlyList<byte[]> Packets);

internal static class V113TeleRockHandler
{
    public static V113UseTeleRockRequest ParseUse(PacketReader reader)
    {
        var rockType = reader.ReadByte();

        if (reader.Remaining == 4)
        {
            return new V113UseTeleRockRequest(rockType, Mode: 0, reader.ReadInt(), string.Empty);
        }

        var mode = reader.ReadByte();
        return mode switch
        {
            0 => new V113UseTeleRockRequest(rockType, mode, reader.ReadInt(), string.Empty),
            1 => new V113UseTeleRockRequest(rockType, mode, 0, reader.ReadMapleString()),
            _ => new V113UseTeleRockRequest(rockType, mode, 0, string.Empty),
        };
    }

    public static V113UseTeleRockResult HandleUseTeleRock(PacketReader reader, MapService maps)
    {
        V113UseTeleRockRequest request;
        try
        {
            request = ParseUse(reader);
        }
        catch (InvalidDataException)
        {
            return Failure(default);
        }

        if (!request.IsMapMode || !maps.MapExists(request.MapId))
        {
            return Failure(request);
        }

        return new V113UseTeleRockResult(
            Handled: true,
            Success: true,
            request,
            request.MapId,
            [V113TrockPackets.MapTransferUseResult(success: true)]);
    }

    private static V113UseTeleRockResult Failure(V113UseTeleRockRequest request)
        => new(
            Handled: true,
            Success: false,
            request,
            WarpMapId: null,
            [V113TrockPackets.MapTransferUseResult(success: false), V113StatsPackets.EnableActions()]);
}
