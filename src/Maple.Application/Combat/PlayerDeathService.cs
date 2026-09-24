using Maple.Application.Maps;
using Maple.Core.Maps;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Combat;

/// <summary>P090：死亡處理結果——先取消的 buff（Java playerDead 前段），再經驗值懲罰。</summary>
public sealed record PlayerDeathOutcome(IReadOnlyList<PlayerBuffCancellation> CancelledBuffs, DeathPenaltyResult Penalty);

/// <summary>
/// P076：玩家由活轉死時的死亡處理（對照 Java <c>PlayerStats.setHp</c> 死亡分支呼叫的
/// <c>MapleCharacter.playerDead</c>）。已移植：取消 buff 區塊（P090）、經驗值區塊（P076，護身符 → 強效護身符 → 扣經驗）；
/// 靈魂之石、事件副本、裝備耐久、金字塔等分支尚未移植。
/// </summary>
public sealed class PlayerDeathService
{
    private readonly MapService _maps;

    public PlayerDeathService(MapService maps)
    {
        _maps = maps;
    }

    public PlayerDeathOutcome OnPlayerDied(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var cancelled = player.CancelBuffsOnDeath();
        var map = _maps.LoadMap(player.Character.MapId);
        var reduced = map.Town || FieldLimitType.RegularExpLoss.Check(map.FieldLimit);
        return new PlayerDeathOutcome(cancelled, player.ApplyDeathPenalty(reduced));
    }
}
