using Maple.Application.Combat;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P107/P113：怪物受傷後的血條封包與收件範圍，對照 Java <c>MapleMonster.damage</c> 的 HPDisplayType 分派——
/// 存活時 0 王血條（全圖）、1 友方怪 DAMAGE_MONSTER（全圖）、-1/2 小血條（全圖）、3 小血條只給 4 秒內攻擊者；
/// 死亡時只有 -1 再送一次 0%。玩家攻擊與持續傷害共用。
/// </summary>
internal static class V113MobHpDisplayPackets
{
    /// <returns>每筆封包與收件者：<c>OnlyTo</c> 為 null 表示全圖，否則只送這些角色。</returns>
    public static IReadOnlyList<(byte[] Packet, IReadOnlyList<int>? OnlyTo)> Build(CombatMobHit hit, CombatMobHpDisplay display)
    {
        if (hit.Killed)
        {
            return display.DisplayType == -1
                ? [(V113CombatPackets.ShowMonsterHp(hit.ObjectId, display.HpPercent), null)]
                : [];
        }

        return display.DisplayType switch
        {
            0 => [(V113CombatPackets.ShowBossHp(hit.MonsterId, display.Hp, display.MaxHp, display.TagColor, display.TagBgColor), null)],
            1 => [(V113CombatPackets.DamageFriendlyMob(hit.ObjectId, hit.RequestedDamage, display.Hp, display.MaxHp), null)],
            -1 or 2 => [(V113CombatPackets.ShowMonsterHp(hit.ObjectId, display.HpPercent), null)],
            3 => [(V113CombatPackets.ShowMonsterHp(hit.ObjectId, display.HpPercent), display.RecentAttackerIds)],
            _ => [],
        };
    }
}
