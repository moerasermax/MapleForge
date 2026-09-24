using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P073（M4-2 世界 tick 第三個消費者）：對照 Java <c>World.handleCooldowns</c>——世界 tick 對「地圖上
/// 每個玩家」要做的逐人處理。Java 只對有玩家的地圖跑這段（<c>map.characterSize() &gt; 0</c>），
/// 這裡直接以 <see cref="IMapSessionRegistry.GetAll"/> 列出場上玩家，沒有玩家自然什麼都不做。
///
/// 目前處理項目：技能冷卻到期 → 移除冷卻 + 送 <c>skillCooldown(skillId, 0)</c> 給該玩家本人（P073）；
/// 地圖持續扣血 → 扣 HP 並送 HP 更新，扣到 0 先送 <c>enableActions</c>（對照 Java <c>PlayerStats.setHp</c>
/// 死亡分支再 <c>updateSingleStat(HP)</c>，P075）；buff 到期 → 送 <c>cancelBuff</c> + 召喚獸/時空門副作用
/// （對照 Java 每個 buff 各自的 <c>BuffTimer</c> 排程取消，這裡以 3 秒 tick 近似；原本只在玩家送封包時才檢查，
/// 完全閒置的玩家 buff 永遠不會到期，P089）。
/// 跟 <see cref="V113MobRespawnHandler"/> 一樣是薄封裝，排程節奏不歸它管。
/// </summary>
public sealed class V113PlayerTickHandler
{
    private readonly SkillService _skills;
    private readonly FieldHazardService _hazards;
    private readonly PlayerDeathService _deaths;
    private readonly V113BuffCancellationEffects _buffEffects;
    private readonly IMapSessionRegistry _mapRegistry;

    public V113PlayerTickHandler(
        SkillService skills,
        FieldHazardService hazards,
        PlayerDeathService deaths,
        V113BuffCancellationEffects buffEffects,
        IMapSessionRegistry mapRegistry)
    {
        _skills = skills;
        _hazards = hazards;
        _deaths = deaths;
        _buffEffects = buffEffects;
        _mapRegistry = mapRegistry;
    }

    public async Task TickPlayersAsync(FieldInstance field, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);

        var entries = _mapRegistry.GetAll(field.MapId);
        foreach (var entry in entries)
        {
            var expired = _skills.ExpireSkillCooldowns(entry.Player, now);
            foreach (var skillId in expired)
            {
                await SendBestEffortAsync(entry, V113SkillPackets.SkillCooldown(skillId, 0), ct).ConfigureAwait(false);
            }

            var cancellations = _skills.CancelExpiredBuffs(entry.Player, now);
            foreach (var cancellation in cancellations)
            {
                await SendBestEffortAsync(entry, V113SkillPackets.CancelBuff(cancellation.Stats), ct).ConfigureAwait(false);
            }

            await _buffEffects.ApplyAsync(entry.Player, field, cancellations, ct).ConfigureAwait(false);

            if (entry.Player.IsAlive)
            {
                // Java handleCooldowns 的 isAlive 區塊順序：Dragon Blood → Berserk → Recovery → hurt。
                await TickDragonBloodAsync(entry, field, entries, now, ct).ConfigureAwait(false);
                await TickRecoveryAsync(entry, field, entries, now, ct).ConfigureAwait(false);
            }
        }

        if (field.HpDecay is null || entries.Count == 0)
        {
            return;
        }

        IReadOnlyList<Player> damaged;
        lock (field)
        {
            damaged = _hazards.ApplyHpDecay(field, entries.Select(static e => e.Player).ToArray(), now);
        }

        foreach (var player in damaged)
        {
            var entry = entries.First(e => ReferenceEquals(e.Player, player));
            if (!player.IsAlive)
            {
                // P076：由活轉死 → 死亡懲罰（enableActions + 護身符/經驗值封包），HP 更新留在最後。
                // P090：playerDead 前段取消的 buff 連帶移除召喚獸。
                var outcome = _deaths.OnPlayerDied(player);
                foreach (var packet in V113DeathPackets.Build(player, outcome))
                {
                    await SendBestEffortAsync(entry, packet, ct).ConfigureAwait(false);
                }

                await _buffEffects.ApplyAsync(player, field, outcome.CancelledBuffs, ct).ConfigureAwait(false);
            }

            await SendBestEffortAsync(
                entry,
                V113StatsPackets.UpdateStats(new[] { new PlayerStatUpdate(PlayerStatKind.Hp, player.Hp) }),
                ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// P093：龍之魂週期扣血。對照 Java <c>doDragonBlood</c>：<c>addHP(-x)</c>（HP 更新給本人）→ <c>showOwnBuffEffect(src, 5)</c>
    /// 給本人 → 沒有 MORPH buff 時 <c>showBuffeffect(id, src, 5)</c> 給同圖其他人；HP 不足則取消 DRAGONBLOOD。
    /// </summary>
    private async Task TickDragonBloodAsync(
        MapPlayerEntry entry,
        FieldInstance field,
        IReadOnlyList<MapPlayerEntry> entries,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var player = entry.Player;
        if (_skills.TryDragonBlood(player, now) is not { } tick)
        {
            return;
        }

        foreach (var cancellation in tick.Cancellations)
        {
            await SendBestEffortAsync(entry, V113SkillPackets.CancelBuff(cancellation.Stats), ct).ConfigureAwait(false);
        }

        await _buffEffects.ApplyAsync(player, field, tick.Cancellations, ct).ConfigureAwait(false);

        if (tick.HpDelta >= 0)
        {
            return;
        }

        await SendBestEffortAsync(
            entry,
            V113StatsPackets.UpdateStats(new[] { new PlayerStatUpdate(PlayerStatKind.Hp, player.Hp) }),
            ct).ConfigureAwait(false);
        await SendBestEffortAsync(entry, V113SkillPackets.ShowOwnBuffEffect(tick.SourceId, 5), ct).ConfigureAwait(false);
        if (player.ActiveBuffs.Any(static b => b.Stat == Core.Skills.MapleBuffStat.MORPH))
        {
            return;
        }

        var foreign = V113SkillPackets.ShowForeignBuffEffect(player.Character.Id, tick.SourceId, 5);
        foreach (var other in entries.Where(e => e.CharId != entry.CharId))
        {
            await SendBestEffortAsync(other, foreign, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// P092：回復術週期回血。對照 Java <c>doRecovery</c> → <c>healHP(x, true)</c>：<c>addHP</c>（HP 更新給本人）→
    /// <c>showOwnHpHealed</c> 給本人 → <c>showHpHealed</c> 給同圖其他人；滿血則取消 RECOVERY buff（cancelBuff 給本人）。
    /// </summary>
    private async Task TickRecoveryAsync(
        MapPlayerEntry entry,
        FieldInstance field,
        IReadOnlyList<MapPlayerEntry> entries,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var player = entry.Player;
        if (_skills.TryRecover(player, now) is not { } tick)
        {
            return;
        }

        foreach (var cancellation in tick.Cancellations)
        {
            await SendBestEffortAsync(entry, V113SkillPackets.CancelBuff(cancellation.Stats), ct).ConfigureAwait(false);
        }

        await _buffEffects.ApplyAsync(player, field, tick.Cancellations, ct).ConfigureAwait(false);

        if (tick.HpDelta <= 0)
        {
            return;
        }

        await SendBestEffortAsync(
            entry,
            V113StatsPackets.UpdateStats(new[] { new PlayerStatUpdate(PlayerStatKind.Hp, player.Hp) }),
            ct).ConfigureAwait(false);
        await SendBestEffortAsync(entry, V113StatsPackets.ShowOwnHpHealed(tick.HpDelta), ct).ConfigureAwait(false);
        var foreign = V113StatsPackets.ShowHpHealed(player.Character.Id, tick.HpDelta);
        foreach (var other in entries.Where(e => e.CharId != entry.CharId))
        {
            await SendBestEffortAsync(other, foreign, ct).ConfigureAwait(false);
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
