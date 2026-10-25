using Maple.Core.Characters;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

/// <summary>P115：對照 Java <c>PlayerStats.recalcLocalStats</c> 的 localmaxhp/localmaxmp。</summary>
public sealed class PlayerCurrentMaxHpTests
{
    [Fact]
    public void MaxHp_AddsEquipHpAndMp()
    {
        var player = MakePlayer(maxHp: 500, maxMp: 300);
        player.Character.Equips.Add(new EquipEntry { Position = -5, ItemId = 1040002, Hp = 100, Mp = 50 });
        player.Character.Equips.Add(new EquipEntry { Position = -1, ItemId = 1002000, Hp = 20 });

        Assert.Equal((short)620, player.MaxHp);
        Assert.Equal((short)350, player.MaxMp);
        Assert.Equal((short)500, player.Character.Stats.MaxHp);
    }

    [Fact]
    public void MaxHp_HyperBodyBuff_AppliesPercentAfterEquips()
    {
        var player = MakePlayer(maxHp: 1000, maxMp: 1000);
        player.Character.Equips.Add(new EquipEntry { Position = -5, ItemId = 1040002, Hp = 1 });
        player.ApplySkillEffect(HyperBody(60), DateTimeOffset.UnixEpoch);

        Assert.Equal((short)1601, player.MaxHp); // (int)(1001 + 0.6*1001)=1601
        Assert.Equal((short)1600, player.MaxMp);
    }

    [Fact]
    public void MaxHp_ClampsAtThirtyThousand()
    {
        var player = MakePlayer(maxHp: 29_000, maxMp: 100);
        player.Character.Equips.Add(new EquipEntry { Position = -5, ItemId = 1040002, Hp = 5000 });

        Assert.Equal((short)30000, player.MaxHp);
    }

    [Fact]
    public void HealHp_CapsAtCurrentMaxHp()
    {
        var player = MakePlayer(maxHp: 500, maxMp: 100);
        player.Character.Equips.Add(new EquipEntry { Position = -5, ItemId = 1040002, Hp = 100 });

        player.HealHp(10_000);

        Assert.Equal((short)600, player.Hp);
    }

    [Fact]
    public void EnforceMaxHpMp_BuffEnds_ClampsHpAndMp()
    {
        var player = MakePlayer(maxHp: 1000, maxMp: 1000);
        player.ApplySkillEffect(HyperBody(60), DateTimeOffset.UnixEpoch);
        player.HealHp(5000);
        player.HealMp(5000);
        Assert.Equal((short)1600, player.Hp);

        player.CancelBuffBySource(1301007);
        var updates = player.EnforceMaxHpMp();

        Assert.Equal((short)1000, player.Hp);
        Assert.Equal((short)1000, player.Mp);
        Assert.Equal(new[] { PlayerStatKind.Mp, PlayerStatKind.Hp }, updates.Select(static u => u.Kind));
    }

    [Fact]
    public void EnforceMaxHpMp_WithinLimits_NoUpdates()
    {
        Assert.Empty(MakePlayer(maxHp: 100, maxMp: 100).EnforceMaxHpMp());
    }

    private static MapleStatEffect HyperBody(int percent)
        => new()
        {
            SourceId = 1301007,
            Level = 1,
            IsOverTime = true,
            DurationMilliseconds = 60_000,
            Statups = [new BuffStatValue(MapleBuffStat.MAXHP, percent), new BuffStatValue(MapleBuffStat.MAXMP, percent)],
        };

    private static Player MakePlayer(short maxHp, short maxMp)
        => new(
            new Character
            {
                Id = 1,
                Name = "Tank",
                Stats = new CharacterStats { Hp = 1, MaxHp = maxHp, Mp = 1, MaxMp = maxMp },
            },
            new Position(0, 0, 0, 0));
}
