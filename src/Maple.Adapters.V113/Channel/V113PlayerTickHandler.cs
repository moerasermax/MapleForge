using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P073（M4-2 世界 tick 第三個消費者）：對照 Java <c>World.handleCooldowns</c>——世界 tick 對「地圖上
/// 每個玩家」要做的逐人處理。Java 只對有玩家的地圖跑這段（<c>map.characterSize() &gt; 0</c>），
/// 這裡直接以 <see cref="IMapSessionRegistry.GetAll"/> 列出場上玩家，沒有玩家自然什麼都不做。
///
/// 目前處理項目：技能冷卻到期 → 移除冷卻 + 送 <c>skillCooldown(skillId, 0)</c> 給該玩家本人。
/// 跟 <see cref="V113MobRespawnHandler"/> 一樣是薄封裝，排程節奏不歸它管。
/// </summary>
public sealed class V113PlayerTickHandler
{
    private readonly SkillService _skills;
    private readonly IMapSessionRegistry _mapRegistry;

    public V113PlayerTickHandler(SkillService skills, IMapSessionRegistry mapRegistry)
    {
        _skills = skills;
        _mapRegistry = mapRegistry;
    }

    public async Task TickPlayersAsync(FieldInstance field, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);

        foreach (var entry in _mapRegistry.GetAll(field.MapId))
        {
            var expired = _skills.ExpireSkillCooldowns(entry.Player, now);
            foreach (var skillId in expired)
            {
                await SendBestEffortAsync(entry, V113SkillPackets.SkillCooldown(skillId, 0), ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task SendBestEffortAsync(MapPlayerEntry entry, byte[] packet, CancellationToken ct)
    {
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
            // 世界 tick 廣播是 best-effort；斷線 session 由中央連線處理生命週期負責清理。
        }
    }
}
