using Maple.Application.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P112（怪物異常狀態系列）：單一 field 的怪物狀態 tick——移除到期狀態並廣播
/// <c>CANCEL_MONSTER_STATUS</c>（對照 Java <c>applyStatus</c> 的 <c>cancelTask</c> → <c>cancelStatus(stat)</c>，
/// 每個狀態各送一包）。排程由 <c>Maple.Host.Shared</c> 的 <c>MobStatusTickHostedService</c> 負責（1 秒）。
/// </summary>
public sealed class V113MobStatusTickHandler
{
    private readonly IMapSessionRegistry _mapRegistry;

    public V113MobStatusTickHandler(IMapSessionRegistry mapRegistry)
    {
        _mapRegistry = mapRegistry;
    }

    public async Task TickAsync(FieldInstance field, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);

        var cancelled = new List<(int ObjectId, MonsterStatus Status)>();
        lock (field)
        {
            foreach (var mob in field.Objects.OfType<Mob>())
            {
                foreach (var effect in mob.ExpireStatuses(now))
                {
                    cancelled.Add((mob.ObjectId, effect.Status));
                }
            }
        }

        if (cancelled.Count == 0)
        {
            return;
        }

        var recipients = _mapRegistry.GetAll(field.MapId);
        foreach (var (objectId, status) in cancelled)
        {
            var packet = V113MonsterStatusCodec.CancelMonsterStatus(objectId, [status]);
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
}
