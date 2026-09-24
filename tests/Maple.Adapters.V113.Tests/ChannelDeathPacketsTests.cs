using Maple.Adapters.V113.Channel;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>P076：死亡封包序列，對照 Java <c>PlayerStats.setHp</c> 死亡分支 + <c>playerDead</c>。</summary>
public sealed class ChannelDeathPacketsTests
{
    [Fact]
    public void UseCharm_MatchesJavaMtscsUseCharmShape()
    {
        var r = new PacketReader(V113DeathPackets.UseCharm(charmsLeft: 3, daysLeft: 0));

        Assert.Equal(V113ChannelSendOp.ShowItemGainInChat, r.ReadShort());
        Assert.Equal(6, r.ReadByte());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(3, r.ReadByte());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void Build_ExpLoss_SendsEnableActionsThenExpUpdate()
    {
        var player = NewPlayer(exp: 1000);
        var penalty = player.ApplyDeathPenalty(reducedExpLoss: false);

        var packets = V113DeathPackets.Build(player, penalty);

        Assert.Equal(
            new[]
            {
                V113StatsPackets.EnableActions(),
                V113StatsPackets.UpdateStats(new[] { new PlayerStatUpdate(PlayerStatKind.Exp, 829) }),
            },
            packets);
    }

    [Fact]
    public void Build_Charm_SendsInventoryRemovalAndUseCharmBeforeExpUpdate()
    {
        var player = NewPlayer(exp: 1000);
        player.GainItem(InventoryType.Cash, Player.SafetyCharmItemId, 1);
        var penalty = player.ApplyDeathPenalty(reducedExpLoss: false);

        var packets = V113DeathPackets.Build(player, penalty);

        Assert.Equal(4, packets.Count);
        Assert.Equal(V113StatsPackets.EnableActions(), packets[0]);
        Assert.Equal(V113RangedMagicAttackPackets.ModifyInventoryQuantity(Assert.Single(penalty.CharmMutations)), packets[1]);
        Assert.Equal(V113DeathPackets.UseCharm(0, 0), packets[2]);
        Assert.Equal(V113StatsPackets.UpdateStats(new[] { new PlayerStatUpdate(PlayerStatKind.Exp, 1000) }), packets[3]);
    }

    private static Player NewPlayer(int exp)
        => new(new Character
        {
            Id = 1,
            Name = "Dead",
            Job = 100,
            Level = 10,
            Exp = exp,
            Stats = new CharacterStats { Hp = 0, MaxHp = 100, Luk = 4 },
        }, new Position(0, 0, 0, 0));
}
