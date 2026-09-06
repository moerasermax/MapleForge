using Maple.Application.Maps;
using Maple.Application.Parties;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal static class V113PartySearchPackets
{
    public static (int MinLevel, int MaxLevel, int MemberNum, int JobMask) ParseStart(PacketReader reader)
    {
        var minLevel = reader.ReadInt();
        var maxLevel = reader.ReadInt();
        var memberNum = reader.ReadInt();
        var jobMask = reader.ReadInt();
        return (minLevel, maxLevel, memberNum, jobMask);
    }
}

/// <summary>
/// 對照 Java <c>PartyHandler.PartySearchStart/PartySearchStop</c> + <c>World.PartySearch</c>：
/// 隊長登記搜尋條件後，立即掃同地圖現有玩家；之後任何玩家進場地圖時再掃一次活躍搜尋。
/// </summary>
public sealed class V113PartySearchHandler
{
    private readonly PartySearchService _service;
    private readonly IMapSessionRegistry _mapRegistry;
    private readonly IPartyRegistry _parties;

    public V113PartySearchHandler(PartySearchService service, IMapSessionRegistry mapRegistry, IPartyRegistry parties)
    {
        _service = service;
        _mapRegistry = mapRegistry;
        _parties = parties;
    }

    public async Task HandleStartAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        var chr = player.Character;
        var (minLevel, maxLevel, memberNum, jobMask) = V113PartySearchPackets.ParseStart(reader);

        var outcome = _service.TryStartSearch(chr.Id, chr.Level, minLevel, maxLevel, memberNum, jobMask);
        if (!outcome.Succeeded)
        {
            if (outcome.RejectionMessage is not null)
            {
                await sendSelf(V113BroadcastPackets.PopupMessage(outcome.RejectionMessage), ct);
            }

            return;
        }

        // 對照 Java World.PartySearch.startSearch：立刻掃描目前同地圖的其他玩家是否已符合條件。
        foreach (var other in _mapRegistry.GetOthers(chr.MapId, chr.Id))
        {
            await TryInviteAsync(other.CharId, other.Character.Level, other.Character.Job, other.Character.MapId, other.SendPacket, ct);
        }
    }

    public void HandleStop(Player player) => _service.StopSearch(player.Character.Id);

    /// <summary>對照 Java <c>MapleMap.addPlayer</c> 尾端呼叫 <c>World.PartySearch.checkPartySearch(chr)</c>。</summary>
    public Task NotifyMapEntryAsync(Player player, Func<byte[], CancellationToken, Task> sendSelf, CancellationToken ct)
    {
        var chr = player.Character;
        return TryInviteAsync(chr.Id, chr.Level, chr.Job, chr.MapId, sendSelf, ct);
    }

    /// <summary>對照 Java <c>MapleMap.removePlayer</c> 呼叫 <c>World.PartySearch.stopSearch(chr)</c>。</summary>
    public void NotifyMapLeave(Player player) => _service.StopSearch(player.Character.Id);

    private async Task TryInviteAsync(
        int candidateCharacterId,
        int candidateLevel,
        int candidateJob,
        int candidateMapId,
        Func<byte[], CancellationToken, Task> sendToCandidate,
        CancellationToken ct)
    {
        var matchedParty = _service.CheckOnMapEntry(candidateCharacterId, candidateLevel, candidateJob, candidateMapId);
        if (matchedParty?.Leader is not { } leader)
        {
            return;
        }

        await sendToCandidate(V113PartyPackets.PartyInvite(matchedParty.Id, leader.Name, auto: true), ct);
    }
}
