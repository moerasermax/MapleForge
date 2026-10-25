using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.Characters;
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
    private readonly PlayerMistService? _playerMists;

    public V113MobStatusTickHandler(IMapSessionRegistry mapRegistry, CombatService combat, PlayerMistService? playerMists = null)
    {
        _mapRegistry = mapRegistry;
        _combat = combat;
        _playerMists = playerMists;
    }

    public async Task TickAsync(FieldInstance field, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);

        var cancelled = new List<(int ObjectId, MonsterStatus Status)>();
        IReadOnlyList<CombatMobHit> dotHits;
        Mist[] expiredMists;
        var mistTick = MistTickResult.Empty;
        lock (field)
        {

            // P131：Java spawnMist 的 tMan.schedule(remove, duration)——到期移除並廣播 REMOVE_MIST。
            expiredMists = field.Objects.OfType<Mist>().Where(m => m.ExpiresAt <= now).ToArray();
            foreach (var mist in expiredMists)
            {
                field.Remove(mist.ObjectId);
            }

            // Java 的毒傷排程與解除排程各自獨立；同一跳先扣毒傷再處理到期，讓到期當秒的最後一跳仍生效。
            dotHits = _combat.TickDamageOverTime(field);

            // P132：玩家霧的週期效果（Java 2000/2500 排程）。排在毒傷之後，剛上的毒下一跳才扣血（同攻擊上毒）；
            // 已到期的霧前面已移除，Tick 本身也會略過。
            if (_playerMists is not null)
            {
                mistTick = _playerMists.Tick(field, now);
            }
            foreach (var mob in field.Objects.OfType<Mob>())
            {
                foreach (var effect in mob.ExpireStatuses(now))
                {
                    cancelled.Add((mob.ObjectId, effect.Status));
                }
            }
        }

        if (cancelled.Count == 0 && dotHits.Count == 0 && expiredMists.Length == 0 && mistTick.IsEmpty)
        {
            return;
        }

        var recipients = _mapRegistry.GetAll(field.MapId);
        foreach (var mist in expiredMists)
        {
            await SendAsync(recipients, V113MistPackets.RemoveMist(mist.ObjectId), ct);
        }

        // P132：霧上毒 → applyStatus 的 broadcastMessage(applyMonsterStatus)；回 MP → addMP 的 updateSingleStat(MP)。
        foreach (var (objectId, status) in mistTick.Poisoned)
        {
            await SendAsync(recipients, V113MonsterStatusCodec.ApplyMonsterStatus(objectId, [status]), ct);
        }

        foreach (var player in mistTick.MpRestored)
        {
            await SendAsync(
                recipients.Where(r => r.CharId == player.Character.Id),
                V113StatsPackets.UpdateStats([new PlayerStatUpdate(PlayerStatKind.Mp, player.Mp)]),
                ct);
        }

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
