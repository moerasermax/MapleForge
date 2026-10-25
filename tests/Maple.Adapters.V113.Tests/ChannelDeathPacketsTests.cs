using Maple.Adapters.V113.Channel;
using Maple.Application.Combat;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Skills;
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

        var packets = V113DeathPackets.Build(player, new PlayerDeathOutcome(Array.Empty<PlayerBuffCancellation>(), penalty));

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

        var packets = V113DeathPackets.Build(player, new PlayerDeathOutcome(Array.Empty<PlayerBuffCancellation>(), penalty));

        Assert.Equal(4, packets.Count);
        Assert.Equal(V113StatsPackets.EnableActions(), packets[0]);
        Assert.Equal(V113RangedMagicAttackPackets.ModifyInventoryQuantity(Assert.Single(penalty.CharmMutations)), packets[1]);
        Assert.Equal(V113DeathPackets.UseCharm(0, 0), packets[2]);
        Assert.Equal(V113StatsPackets.UpdateStats(new[] { new PlayerStatUpdate(PlayerStatKind.Exp, 1000) }), packets[3]);
    }

    [Fact]
    public void Build_CancelledBuffs_SentAfterEnableActionsBeforeExp()
    {
        // P090：Java playerDead 取消 buff 在經驗值區塊之前。
        var player = NewPlayer(exp: 1000);
        var cancelled = new[] { new PlayerBuffCancellation(3111005, new[] { MapleBuffStat.SUMMON }) };

        var packets = V113DeathPackets.Build(player, new PlayerDeathOutcome(cancelled, player.ApplyDeathPenalty(reducedExpLoss: false)));

        Assert.Equal(V113StatsPackets.EnableActions(), packets[0]);
        Assert.Equal(V113SkillPackets.CancelBuff(new[] { MapleBuffStat.SUMMON }), packets[1]);
        Assert.Equal(V113StatsPackets.UpdateStats(new[] { new PlayerStatUpdate(PlayerStatKind.Exp, 829) }), packets[2]);
    }

    [Fact]
    public void SoulStone_RevivesAtXPercent_SkipsPenaltyAndBuffCancel()
    {
        // P205：Java playerDead 開頭的 SOUL_STONE 分支。
        var player = NewPlayer(exp: 1000);
        player.Character.Stats.MaxHp = 1000;
        var soulStone = new MapleStatEffect
        {
            SourceId = 22181003,
            Level = 1,
            IsOverTime = true,
            X = 40,
            DurationMilliseconds = 60_000,
            Statups = [new BuffStatValue(MapleBuffStat.SOUL_STONE, 1)],
        };
        player.ApplyBuffStats(soulStone, DateTimeOffset.UtcNow);
        var service = new PlayerDeathService(
            new Maple.Application.Maps.MapService(new NullDataProvider()),
            new SingleSkillCatalog(new MapleSkill { Id = 22181003, Effects = [soulStone] }));

        var outcome = service.OnPlayerDied(player);

        Assert.True(outcome.SoulStoneRevived);
        Assert.Empty(outcome.CancelledBuffs);
        Assert.Equal((short)400, player.Hp);
        Assert.Equal(1000, player.Character.Exp);
        Assert.Equal(1, player.GetBuffValue(MapleBuffStat.SOUL_STONE));
        Assert.Equal(
            new[]
            {
                V113StatsPackets.EnableActions(),
                V113BroadcastPackets.ServerNotice(5, PlayerDeathOutcome.SoulStoneMessage),
            },
            V113DeathPackets.Build(player, outcome));
    }

    private sealed class SingleSkillCatalog(MapleSkill skill) : ISkillCatalog
    {
        public MapleSkill? GetSkill(int skillId) => skillId == skill.Id ? skill : null;
    }

    private sealed class NullDataProvider : Core.Data.IDataProvider
    {
        public Core.Data.IDataNode GetRoot(string fileName) => throw new NotSupportedException();

        public Core.Data.IDataNode? GetAt(string fileName, string path) => null;
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
