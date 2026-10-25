using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P112（怪物異常狀態系列）：單一 field 的怪物狀態 tick——移除到期狀態並廣播
/// <c>CANCEL_MONSTER_STATUS</c>（對照 Java <c>applyStatus</c> 的 <c>cancelTask</c> → <c>cancelStatus(stat)</c>，
/// 每個狀態各送一包）。P113：同一跳也處理中毒／忍者伏擊持續傷害並送血條。
/// 排程由 <c>Maple.Host.Shared</c> 的 <c>MobStatusTickHostedService</c> 負責（1 秒）。
/// </summary>
public sealed class V113MobStatusTickHandler
{
    private readonly IMapSessionRegistry _mapRegistry;
    private readonly CombatService _combat;

    public V113MobStatusTickHandler(IMapSessionRegistry mapRegistry, CombatService combat)
    {
        _mapRegistry = mapRegistry;
        _combat = combat;
    }

    public async Task TickAsync(FieldInstance field, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);

        var cancelled = new List<(int ObjectId, MonsterStatus Status)>();
        IReadOnlyList<CombatMobHit> dotHits;
        lock (field)
        {
            // Java 的毒傷排程與解除排程各自獨立；同一跳先扣毒傷再處理到期，讓到期當秒的最後一跳仍生效。
            dotHits = _combat.TickDamageOverTime(field);
            foreach (var mob in field.Objects.OfType<Mob>())
            {
                foreach (var effect in mob.ExpireStatuses(now))
                {
                    cancelled.Add((mob.ObjectId, effect.Status));
                }
            }
        }

        if (cancelled.Count == 0 && dotHits.Count == 0)
        {
            return;
        }

        var recipients = _mapRegistry.GetAll(field.MapId);
        foreach (var hit in dotHits)
        {
            if (hit.HpDisplay is not { } display)
            {
                continue;
            }

            foreach (var (packet, onlyTo) in V113MobHpDisplayPackets.Build(hit, display))
            {
                await SendAsync(onlyTo is null ? recipients : recipients.Where(r => onlyTo.Contains(r.CharId)), packet, ct);
            }
        }

        foreach (var (objectId, status) in cancelled)
        {
            await SendAsync(recipients, V113MonsterStatusCodec.CancelMonsterStatus(objectId, [status]), ct);
        }
    }

    private static async Task SendAsync(IEnumerable<MapPlayerEntry> recipients, byte[] packet, CancellationToken ct)
    {
        foreach (var recipient in recipients)
        {
            try
            {
                await recipient.SendPacket(packet, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // best-effort，同 V113DropExpiryHandler。
            }
        }
    }
}
