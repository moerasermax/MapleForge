using Maple.Application.Combat;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Items;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Combat;

/// <summary>P136：對照 Java PlayerStats.recalcLocalStats／calculateMaxBaseDamage。</summary>
public sealed class PlayerLocalStatsCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BaseAndEquips_AreSummed_MagicIncludesEquipIntAndMatk()
    {
        var player = MakePlayer(job: 200, str: 4, dex: 4, intel: 100, luk: 10);
        player.Character.Equips.Add(new EquipEntry { Position = WeaponSlot, ItemId = 1372005, Int = 5, Matk = 50, Watk = 1, Speed = 10 });

        var stats = Calculator().Calculate(player);

        Assert.Equal(105, stats.Int);
        Assert.Equal(100 + 50 + 5, stats.Magic);
        Assert.Equal(1, stats.Watk);
        Assert.Equal(110, stats.Speed);
        Assert.Equal(4 + 105 + 10, stats.Hands);
    }

    [Theory]
    [InlineData(412, 30)]
    [InlineData(1411, 30)]
    [InlineData(522, 20)]
    [InlineData(500, 20)]
    [InlineData(510, 0)]
    public void JobBaseWatk_StarsAndBullets(int job, int expected)
        => Assert.Equal(expected, Calculator().Calculate(MakePlayer(job: job)).Watk);

    [Fact]
    public void MapleWarrior_MultipliesBaseStatsOnly_AndFeedsMagic()
    {
        var player = MakePlayer(job: 212, intel: 500, str: 4, dex: 4, luk: 100);
        player.Character.Equips.Add(new EquipEntry { Position = WeaponSlot, ItemId = 1382000, Int = 100 });
        Buff(player, 2121000, MapleBuffStat.MAPLE_WARRIOR, 10);

        var stats = Calculator().Calculate(player);

        Assert.Equal(600 + 50, stats.Int); // +10% × 基礎 500
        Assert.Equal(110, stats.Luk);
        Assert.Equal(500 + 100 + 50, stats.Magic);
    }

    [Fact]
    public void MagicCappedAt1999_BeforeBuffs()
    {
        var player = MakePlayer(job: 212, intel: 2500);
        Buff(player, 2001003, MapleBuffStat.MATK, 20);

        Assert.Equal(1999 + 20, Calculator().Calculate(player).Magic);
    }

    [Fact]
    public void EchoOfHero_AddsPercentOfWatkAndMagic()
    {
        var player = MakePlayer(job: 112);
        player.Character.Equips.Add(new EquipEntry { Position = WeaponSlot, ItemId = 1402000, Watk = 100 });
        Buff(player, 1005, MapleBuffStat.ECHO_OF_HERO, 4);

        Assert.Equal(104, Calculator().Calculate(player).Watk);
    }

    [Fact]
    public void BowmasterExpert_AndBlessingOfFairy_AddWatk()
    {
        var player = MakePlayer(job: 312);
        player.ChangeSkillLevel(3120005, 1, 1);
        player.ChangeSkillLevel(12, 1, 1);
        var catalog = new InMemorySkillCatalog(
        [
            new MapleSkill { Id = 3120005, Effects = [new MapleStatEffect { SourceId = 3120005, Level = 1, X = 10 }] },
            new MapleSkill { Id = 12, Effects = [new MapleStatEffect { SourceId = 12, Level = 1, X = 3, Y = 2 }] },
        ]);

        var stats = new PlayerLocalStatsCalculator(catalog).Calculate(player);

        Assert.Equal(13, stats.Watk);
        Assert.Equal(4 + 2, stats.Magic);
    }

    [Fact]
    public void Amplification_AndBerserkFury_MultiplyDamageRate()
    {
        var player = MakePlayer(job: 212);
        player.ChangeSkillLevel(2110001, 1, 1);
        Buff(player, 9999999, MapleBuffStat.BERSERK_FURY, 1);
        var catalog = new InMemorySkillCatalog(
            [new MapleSkill { Id = 2110001, Effects = [new MapleStatEffect { SourceId = 2110001, Level = 1, X = 150, Y = 130 }] }]);

        var stats = new PlayerLocalStatsCalculator(catalog).Calculate(player);

        Assert.Equal(100 * 1.3 * 2.0, stats.DamageRate, 6);
        Assert.Equal(stats.DamageRate, stats.BossDamageRate, 6);
    }

    [Fact]
    public void ChargeBuff_UsesSourceSkillDamage()
    {
        var player = MakePlayer(job: 122);
        var charge = new MapleStatEffect
        {
            SourceId = 1211004,
            Level = 1,
            Damage = 125,
            IsOverTime = true,
            DurationMilliseconds = 60_000,
            Statups = [new BuffStatValue(MapleBuffStat.WK_CHARGE, 1)],
        };
        player.ApplyBuffStats(charge, T0);
        var catalog = new InMemorySkillCatalog([new MapleSkill { Id = 1211004, Effects = [charge] }]);

        Assert.Equal(125.0, new PlayerLocalStatsCalculator(catalog).Calculate(player).DamageRate, 6);
    }

    [Fact]
    public void SpeedAndJump_AreCapped()
    {
        var player = MakePlayer();
        player.Character.Equips.Add(new EquipEntry { Position = -7, ItemId = 1072000, Speed = 30, Jump = 30 });
        Buff(player, 4101004, MapleBuffStat.SPEED, 20);

        var stats = Calculator().Calculate(player);

        Assert.Equal(140, stats.Speed);
        Assert.Equal(123, stats.Jump);
    }

    [Fact]
    public void MaxBaseDamage_Warrior_OneHandedSword()
    {
        var player = MakePlayer(job: 110, str: 100, dex: 20);
        player.Character.Equips.Add(new EquipEntry { Position = WeaponSlot, ItemId = 1302000, Watk = 50 });

        // (1.2 * 100 + 20) * 50 / 100 = 70
        Assert.Equal(70f, Calculator().Calculate(player).MaxBaseDamage, 3);
    }

    [Fact]
    public void MaxBaseDamage_ThiefClaw_UsesLukAndDexPlusStr()
    {
        var player = MakePlayer(job: 410, str: 4, dex: 30, luk: 200);
        player.Character.Equips.Add(new EquipEntry { Position = WeaponSlot, ItemId = 1472000, Watk = 15 });

        // 飛鏢基礎 30 + 拳套 15 = 45；(1.75 * 200 + 34) * 45 / 100 = 172.8
        Assert.Equal(172.8f, Calculator().Calculate(player).MaxBaseDamage, 3);
    }

    [Fact]
    public void MaxBaseDamage_Mage_UsesMagic_AndPolearmQuirk()
    {
        var mage = MakePlayer(job: 212, intel: 100, luk: 10);
        mage.Character.Equips.Add(new EquipEntry { Position = WeaponSlot, ItemId = 1382000, Watk = 1, Matk = 100 });
        // 長杖係數 1.0：(1.0 * 100 + 10) * magic(200) / 100 = 220
        Assert.Equal(220f, Calculator().Calculate(mage).MaxBaseDamage, 3);

        var spearman = MakePlayer(job: 130, str: 100, dex: 20);
        spearman.Character.Equips.Add(new EquipEntry { Position = WeaponSlot, ItemId = 1442000, Watk = 50 });
        // Java「矛」(44)：STR/DEX ×1.25 → (1.49 * 125 + 25) * 50 / 100 = 105.625
        Assert.Equal(105.625f, Calculator().Calculate(spearman).MaxBaseDamage, 3);
    }

    [Fact]
    public void MaxBaseDamage_NoWatk_IsOne()
        => Assert.Equal(1f, Calculator().Calculate(MakePlayer(job: 100)).MaxBaseDamage);

    [Theory]
    [InlineData(1302000, WeaponType.OneHandedSword, 1.2f, 20)]
    [InlineData(1372000, WeaponType.Wand, 1.0f, 25)]
    [InlineData(1432000, WeaponType.Spear, 1.49f, 20)]
    [InlineData(1452000, WeaponType.Bow, 1.3f, 15)]
    [InlineData(1482000, WeaponType.Knuckle, 1.7f, 20)]
    [InlineData(1092000, WeaponType.None, 0f, 0)]
    public void WeaponTypes_MatchJavaTable(int itemId, WeaponType type, float multiplier, int mastery)
    {
        Assert.Equal(type, WeaponTypes.FromItemId(itemId));
        Assert.Equal(multiplier, type.MaxDamageMultiplier());
        Assert.Equal(mastery, type.BaseMastery());
    }

    private const short WeaponSlot = PlayerLocalStatsCalculator.WeaponSlot;

    [Theory]
    [InlineData(110, 1000001, 12, 0)]
    [InlineData(210, 2000001, 0, 24)] // 法師 MP x×2
    [InlineData(1210, 12000001, 0, 0)] // 非強化技能
    public void ComputeLevelUpBonus_MatchesJavaLevelUp(int job, int skillId, int hp, int mp)
    {
        // P165：Java levelUp 的強化最大 HP/MP 技能加成。
        var catalog = new InMemorySkillCatalog([new MapleSkill { Id = skillId, Effects = [new MapleStatEffect { SourceId = skillId, Level = 1, X = 12 }] }]);
        var player = MakePlayer(job: job, intel: 30);
        player.ChangeSkillLevel(skillId, 1, 10);

        var bonus = new PlayerLocalStatsCalculator(catalog).ComputeLevelUpBonus(player);

        Assert.Equal(new LevelUpBonus(hp, mp, 30), bonus);
    }

    private static PlayerLocalStatsCalculator Calculator() => new(new InMemorySkillCatalog([]));

    private static void Buff(Player player, int sourceId, MapleBuffStat stat, int value)
        => player.ApplyBuffStats(
            new MapleStatEffect
            {
                SourceId = sourceId,
                Level = 1,
                IsOverTime = true,
                DurationMilliseconds = 60_000,
                Statups = [new BuffStatValue(stat, value)],
            },
            T0);

    private static Player MakePlayer(int job = 100, short str = 4, short dex = 4, short intel = 4, short luk = 4)
    {
        var character = new Character { Id = 1, Name = "Stat", Job = (short)job };
        character.Stats.Str = str;
        character.Stats.Dex = dex;
        character.Stats.Int = intel;
        character.Stats.Luk = luk;
        return new Player(character, new Position(0, 0, 0, 0));
    }
}
