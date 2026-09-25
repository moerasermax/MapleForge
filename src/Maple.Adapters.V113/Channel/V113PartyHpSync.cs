using Maple.Application.Maps;
using Maple.Application.Parties;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P095：HP 變動時同步隊伍血條。對照 Java <c>PlayerStats.setHp</c>（非 silent）→ <c>MapleCharacter.updatePartyMemberHP</c>：
/// 對「同地圖、同頻道」的隊友送 <c>updatePartyMemberHP(id, hp, currentMaxHp)</c>。MapleForge 每頻道一個 process，
/// <see cref="IMapSessionRegistry"/> 本身就只含本頻道玩家，同地圖過濾即可。best-effort。
/// </summary>
public sealed class V113PartyHpSync
{
    private readonly IPartyRegistry _parties;
    private readonly IMapSessionRegistry _mapRegistry;

    public V113PartyHpSync(IPartyRegistry parties, IMapSessionRegistry mapRegistry)
    {
        _parties = parties;
        _mapRegistry = mapRegistry;
    }

    /// <summary>P099：HP 與 <paramref name="hpBefore"/> 不同才同步（道具/技能這類「不一定動到 HP」的路徑用）。</summary>
    public Task BroadcastIfChangedAsync(Player player, short hpBefore, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.Hp == hpBefore ? Task.CompletedTask : BroadcastAsync(player, ct);
    }

    public async Task BroadcastAsync(Player player, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(player);

        var party = _parties.GetPartyForCharacter(player.Character.Id);
        if (party is null)
        {
            return;
        }

        var packet = V113PartyPackets.UpdatePartyMemberHp(player.Character.Id, player.Hp, player.MaxHp);
        foreach (var entry in _mapRegistry.GetOthers(player.Character.MapId, player.Character.Id))
        {
            if (party.GetMember(entry.CharId) is null)
            {
                continue;
            }

            try
            {
                await entry.SendPacket(packet, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // best-effort；斷線 session 由中央連線處理生命週期負責清理。
            }
        }
    }
}
