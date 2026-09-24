using Maple.Application.Maps;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Combat;

/// <summary>
/// P076：玩家由活轉死時的死亡處理（對照 Java <c>PlayerStats.setHp</c> 死亡分支呼叫的
/// <c>MapleCharacter.playerDead</c>）。目前只移植經驗值區塊（護身符 → 強效護身符 → 扣經驗）；
/// 靈魂之石、事件副本、取消 buff、裝備耐久、金字塔等分支尚未移植。
/// </summary>
public sealed class PlayerDeathService
{
    private readonly MapService _maps;

    public PlayerDeathService(MapService maps)
    {
        _maps = maps;
    }

    public DeathPenaltyResult OnPlayerDied(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var map = _maps.LoadMap(player.Character.MapId);
        var reduced = map.Town || FieldLimitType.RegularExpLoss.Check(map.FieldLimit);
        return player.ApplyDeathPenalty(reduced);
    }
}
