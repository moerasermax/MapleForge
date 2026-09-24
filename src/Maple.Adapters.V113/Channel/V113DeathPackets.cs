using Maple.Application.Combat;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P076：玩家死亡時的封包序列，對照 Java <c>PlayerStats.setHp</c> 死亡分支：先 <c>enableActions</c>，再
/// <c>playerDead</c>（護身符：<c>removeById</c> 背包更新 + <c>MTSCSPacket.useCharm</c>；最後一律
/// <c>updateSingleStat(EXP)</c>）。HP 更新由呼叫端在這串封包之後送出（Java <c>addHP</c> 回到呼叫端才送）。
/// </summary>
internal static class V113DeathPackets
{
    public static IReadOnlyList<byte[]> Build(Player player, PlayerDeathOutcome outcome)
    {
        var packets = new List<byte[]> { V113StatsPackets.EnableActions() };

        // P090：playerDead 前段取消的 buff（dispelSkill(0) + MORPH/MONSTER_RIDING/SUMMON/PUPPET）。
        foreach (var cancellation in outcome.CancelledBuffs)
        {
            packets.Add(V113SkillPackets.CancelBuff(cancellation.Stats));
        }

        var penalty = outcome.Penalty;
        if (penalty.Kind == DeathPenaltyKind.CharmConsumed)
        {
            foreach (var mutation in penalty.CharmMutations)
            {
                packets.Add(V113RangedMagicAttackPackets.ModifyInventoryQuantity(mutation));
            }

            packets.Add(UseCharm((byte)penalty.CharmsLeft, daysLeft: 0));
        }

        packets.Add(V113StatsPackets.UpdateStats(new[] { new PlayerStatUpdate(PlayerStatKind.Exp, player.Character.Exp) }));
        return packets;
    }

    /// <summary>對照 Java <c>MTSCSPacket.useCharm(charmsleft, daysleft)</c>：<c>SHOW_ITEM_GAIN_INCHAT(0xC7)</c> + 6 + 1 + 剩餘數 + 天數。</summary>
    public static byte[] UseCharm(byte charmsLeft, byte daysLeft)
    {
        var w = new PacketWriter(6);
        w.WriteShort(V113ChannelSendOp.ShowItemGainInChat);
        w.WriteByte(6);
        w.WriteByte(1);
        w.WriteByte(charmsLeft);
        w.WriteByte(daysLeft);
        return w.ToArray();
    }
}
