using Maple.Application.Maps;
using Maple.Core.Maps;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Combat;

/// <summary>
/// P090：死亡處理結果——先取消的 buff（Java playerDead 前段），再經驗值懲罰。
/// P205：<see cref="SoulStoneRevived"/> 為真時 Java 在最前面就 return（不取消 buff、不扣經驗、不通知事件），
/// 呼叫端送黃字提示並換到同地圖 0 號傳送點。
/// </summary>
public sealed record PlayerDeathOutcome(
    IReadOnlyList<PlayerBuffCancellation> CancelledBuffs,
    DeathPenaltyResult Penalty,
    bool SoulStoneRevived = false)
{
    /// <summary>Java <c>dropMessage(5, ...)</c> 的靈魂之石復活提示。</summary>
    public const string SoulStoneMessage = "你已經透過靈魂之石復活了。";
}

/// <summary>
/// P076：玩家由活轉死時的死亡處理（對照 Java <c>PlayerStats.setHp</c> 死亡分支呼叫的
/// <c>MapleCharacter.playerDead</c>）。已移植：取消 buff 區塊（P090）、經驗值區塊（P076，護身符 → 強效護身符 → 扣經驗）；
/// P205：靈魂之石分支。事件副本、裝備耐久、金字塔等分支尚未移植。
/// </summary>
public sealed class PlayerDeathService
{
    private readonly MapService _maps;
    private readonly ISkillCatalog? _skills;

    public PlayerDeathService(MapService maps, ISkillCatalog? skills = null)
    {
        _maps = maps;
        _skills = skills;
    }

    public PlayerDeathOutcome OnPlayerDied(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        // P205：Java playerDead 開頭——getStatForBuff(SOUL_STONE) → setHp((getMaxHp() / 100) * x)、setStance(0)、
        // changeMap(同圖, portal 0)、return（stance 由換圖重設）。buff 不取消（Java 也沒取消），持續期間內可重複觸發。
        if (player.ActiveBuffs.FirstOrDefault(static b => b.Stat == MapleBuffStat.SOUL_STONE) is { } soulStone)
        {
            var x = _skills?.GetSkill(soulStone.SourceId)?.GetEffect(soulStone.SkillLevel)?.X ?? soulStone.Value;
            player.Character.Stats.Hp = (short)Math.Clamp(player.MaxHp / 100 * x, 0, player.MaxHp);
            return new PlayerDeathOutcome([], new DeathPenaltyResult(DeathPenaltyKind.ExemptJob, 0, 0, []), SoulStoneRevived: true);
        }

        var cancelled = player.CancelBuffsOnDeath();
        var map = _maps.LoadMap(player.Character.MapId);
        var reduced = map.Town || FieldLimitType.RegularExpLoss.Check(map.FieldLimit);
        return new PlayerDeathOutcome(cancelled, player.ApplyDeathPenalty(reduced));
    }
}
