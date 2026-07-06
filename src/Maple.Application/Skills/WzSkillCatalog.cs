using System.Collections.Concurrent;
using Maple.Core.Data;
using Maple.Core.Skills;

namespace Maple.Application.Skills;

/// <summary>Skill.wz/String.wz 技能資料源；對照 Java SkillFactory + MapleStatEffect.loadFromData。</summary>
public sealed class WzSkillCatalog : ISkillCatalog
{
    private readonly IDataProvider _data;
    private readonly ConcurrentDictionary<int, CachedSkill> _cache = new();

    public WzSkillCatalog(IDataProvider data)
    {
        _data = data;
    }

    public MapleSkill? GetSkill(int skillId)
        => _cache.GetOrAdd(skillId, id => new CachedSkill(LoadSkill(id))).Skill;

    private MapleSkill? LoadSkill(int skillId)
    {
        IDataNode? skillNode;
        try
        {
            skillNode = _data.GetAt("Skill", $"{GetSkillImage(skillId)}/skill/{skillId}");
        }
        catch (InvalidDataException)
        {
            return null;
        }

        if (skillNode is null)
        {
            return null;
        }

        var isBuff = IsBuffSkill(skillId, skillNode);
        var effects = LoadEffects(skillId, skillNode["level"], isBuff);
        var masterLevel = GetInt(skillNode, "masterLevel", 0);

        return new MapleSkill
        {
            Id = skillId,
            Name = LoadSkillName(skillId),
            MasterLevel = masterLevel,
            IsChargeSkill = skillNode["keydown"] is not null,
            IsTimeLimited = GetInt(skillNode, "timeLimited", 0) > 0,
            Effects = effects,
        };
    }

    private IReadOnlyList<MapleStatEffect> LoadEffects(int skillId, IDataNode? levelRoot, bool isBuff)
    {
        if (levelRoot is null)
        {
            return Array.Empty<MapleStatEffect>();
        }

        var effects = new List<MapleStatEffect>();
        foreach (var (name, levelNode) in levelRoot.Children.OrderBy(static p => ParseInt(p.Key, 0)))
        {
            var level = (byte)Math.Clamp(ParseInt(name, effects.Count + 1), byte.MinValue, byte.MaxValue);
            effects.Add(LoadEffect(skillId, levelNode, isBuff, level));
        }

        return effects;
    }

    private MapleStatEffect LoadEffect(int skillId, IDataNode source, bool isBuff, byte level)
    {
        var duration = GetInt(source, "time", -1);
        if (duration > -1)
        {
            duration *= 1000;
        }

        var hp = (short)Math.Clamp(GetInt(source, "hp", 0), short.MinValue, short.MaxValue);
        var mp = (short)Math.Clamp(GetInt(source, "mp", 0), short.MinValue, short.MaxValue);
        var hpRate = GetInt(source, "hpR", 0) / 100.0;
        var mpRate = GetInt(source, "mpR", 0) / 100.0;
        var hpCon = (short)Math.Clamp(GetInt(source, "hpCon", 0), short.MinValue, short.MaxValue);
        var mpCon = (short)Math.Clamp(GetInt(source, "mpCon", 0), short.MinValue, short.MaxValue);
        var x = GetInt(source, "x", 0);
        var y = GetInt(source, "y", 0);
        var z = GetInt(source, "z", 0);
        var prop = GetInt(source, "prop", 100);
        var morph = GetInt(source, "morph", 0);
        var overTime = isBuff || IsMorphSkill(skillId, morph) || IsFinalAttackSkill(skillId);

        if (skillId == 9001004)
        {
            duration = 60 * 120 * 1000;
        }
        else if (skillId is 5211006 or 5220011)
        {
            duration = 60 * 120000;
        }

        var statups = new List<BuffStatValue>();
        var watk = ReadShort(source, "pad");
        var wdef = ReadShort(source, "pdd");
        var matk = ReadShort(source, "mad");
        var mdef = ReadShort(source, "mdd");
        var acc = ReadShort(source, "acc");
        var avoid = ReadShort(source, "eva");
        var speed = ReadShort(source, "speed");
        var jump = ReadShort(source, "jump");
        var mhpR = GetInt(source, "mhpR", 0);
        var mmpR = GetInt(source, "mmpR", 0);
        var mhpTemp = GetInt(source, "mhp_temp", 0);
        var mmpTemp = GetInt(source, "mmp_temp", 0);

        if (overTime && !IsEnergyCharge(skillId))
        {
            AddIfNotZero(statups, MapleBuffStat.WATK, watk);
            AddIfNotZero(statups, MapleBuffStat.WDEF, wdef);
            AddIfNotZero(statups, MapleBuffStat.MATK, matk);
            AddIfNotZero(statups, MapleBuffStat.MDEF, mdef);
            AddIfNotZero(statups, MapleBuffStat.ACC, acc);
            AddIfNotZero(statups, MapleBuffStat.AVOID, avoid);
            AddIfNotZero(statups, MapleBuffStat.SPEED, speed);
            AddIfNotZero(statups, MapleBuffStat.JUMP, jump);
            AddIfNotZero(statups, MapleBuffStat.MAXHP, mhpR + mhpTemp);
            AddIfNotZero(statups, MapleBuffStat.MAXMP, mmpR + mmpTemp);
            AddIfNotZero(statups, MapleBuffStat.EXPRATE, GetInt(source, "expBuff", 0));
            AddIfNotZero(statups, MapleBuffStat.ACASH_RATE, GetInt(source, "cashBuff", 0));
            AddIfNotZero(statups, MapleBuffStat.DROP_RATE, GetInt(source, "itemupbyitem", 0) * 200);
            AddIfNotZero(statups, MapleBuffStat.MESO_RATE, GetInt(source, "mesoupbyitem", 0) * 200);
            AddIfNotZero(statups, MapleBuffStat.BERSERK_FURY, GetInt(source, "berserk2", 0));
            AddIfNotZero(statups, MapleBuffStat.BOOSTER, GetInt(source, "booster", 0));
            AddIfNotZero(statups, MapleBuffStat.ILLUSION, GetInt(source, "illusion", 0));
        }

        AddSkillSpecificStatups(skillId, x, y, prop, statups);

        if (IsMonsterRiding(skillId))
        {
            statups.Add(new BuffStatValue(MapleBuffStat.MONSTER_RIDING, 1));
        }

        return new MapleStatEffect
        {
            SourceId = skillId,
            Level = level,
            IsOverTime = overTime || statups.Count > 0,
            DurationMilliseconds = duration,
            Hp = hp,
            Mp = mp,
            HpRate = skillId == 1311006 ? -x / 100.0 : hpRate,
            MpRate = mpRate,
            HpCon = hpCon,
            MpCon = mpCon,
            Watk = watk,
            Wdef = wdef,
            Matk = matk,
            Mdef = mdef,
            Acc = acc,
            Avoid = avoid,
            Speed = speed,
            Jump = jump,
            X = x,
            Y = y,
            Z = z,
            CooldownSeconds = GetInt(source, "cooltime", 0),
            MoveTo = GetInt(source, "moveTo", -1),
            Statups = statups.ToArray(),
            IsCombo = skillId is 1111002 or 11111001 or 21000000,
            IsFinalAttack = IsFinalAttackSkill(skillId),
            IsFieldObjectSkill = IsFieldObjectSkill(skillId),
        };
    }

    private string LoadSkillName(int skillId)
    {
        try
        {
            var node = _data.GetAt("String", $"Skill.img/{skillId:D7}/name");
            return node?.Value is string name ? name : string.Empty;
        }
        catch (InvalidDataException)
        {
            return string.Empty;
        }
    }

    private static string GetSkillImage(int skillId)
        => $"{skillId / 10000:D3}.img";

    private static bool IsBuffSkill(int skillId, IDataNode skillNode)
    {
        var skillType = GetInt(skillNode, "skillType", -1);
        if (skillType != -1)
        {
            return skillType == 2;
        }

        var action = skillNode["action"];
        var isBuff = (skillNode["effect"] is not null && skillNode["hit"] is null && skillNode["ball"] is null)
            || GetString(action, "0") == "alert2";

        if (IsForcedNonBuffSkill(skillId))
        {
            return false;
        }

        return isBuff || IsForcedBuffSkill(skillId);
    }

    private static bool IsForcedNonBuffSkill(int skillId)
        => skillId is 2301002 or 2111003 or 12111005 or 2111002 or 4211001 or 2121001 or 2221001 or 2321001;

    private static bool IsForcedBuffSkill(int skillId)
        => skillId is 1004 or 10001004 or 20001004 or 20011004 or 30001004
            or 1026 or 10001026 or 20001026 or 20011026 or 30001026
            or 9101004 or 1111002 or 11111001 or 12101005 or 4211003 or 4111001
            or 15111002 or 5111005 or 5121003 or 13111005 or 21000000 or 21101003
            or 5211001 or 5211002 or 5220002 or 5001005 or 15001003 or 5211006 or 5220011
            or 5110001 or 15100004 or 5121009 or 15111005 or 22121001 or 22131001
            or 22141002 or 22151002 or 22151003 or 22171000 or 22171004 or 22181000
            or 22181003 or 4331003 or 15101006 or 15111006 or 4321000 or 1320009
            or 35120000 or 35001002 or 9001004 or 4341002 or 32001003 or 32120000
            or 32101002 or 32110000 or 32101003 or 32120001 or 35101007 or 35121006
            or 35001001 or 35101009 or 35111007 or 35121005 or 35121013 or 35101002
            or 33111003 or 1211009 or 1111007 or 1311007;

    private static void AddSkillSpecificStatups(int skillId, int x, int y, int prop, List<BuffStatValue> statups)
    {
        switch (skillId)
        {
            case 2001002:
            case 12001001:
                statups.Add(new BuffStatValue(MapleBuffStat.MAGIC_GUARD, x));
                break;
            case 2301003:
                statups.Add(new BuffStatValue(MapleBuffStat.INVINCIBLE, x));
                break;
            case 9001004:
                statups.Add(new BuffStatValue(MapleBuffStat.DARKSIGHT, 1));
                break;
            case 13101006:
                statups.Add(new BuffStatValue(MapleBuffStat.WIND_WALK, x));
                break;
            case 4001003:
            case 14001003:
                statups.Add(new BuffStatValue(MapleBuffStat.DARKSIGHT, x));
                break;
            case 4211003:
                statups.Add(new BuffStatValue(MapleBuffStat.PICKPOCKET, x));
                break;
            case 4211005:
                statups.Add(new BuffStatValue(MapleBuffStat.MESOGUARD, x));
                break;
            case 4111001:
                statups.Add(new BuffStatValue(MapleBuffStat.MESOUP, x));
                break;
            case 4111002:
            case 14111000:
                statups.Add(new BuffStatValue(MapleBuffStat.SHADOWPARTNER, x));
                break;
            case 11101002:
            case 21120002:
                statups.Add(new BuffStatValue(MapleBuffStat.FINAL_MELEE_ATTACK, x));
                break;
            case 13101002:
                statups.Add(new BuffStatValue(MapleBuffStat.FINAL_SHOOT_ATTACK, x));
                break;
            case 3101004:
            case 3201004:
            case 2311002:
            case 13101003:
            case 33101003:
            case 8001:
                statups.Add(new BuffStatValue(MapleBuffStat.SOULARROW, x));
                break;
            case 1211006:
            case 1211003:
            case 1211004:
            case 1211005:
            case 1211007:
            case 1211008:
            case 1221003:
            case 1221004:
            case 11111007:
            case 21111005:
            case 15101006:
                statups.Add(new BuffStatValue(MapleBuffStat.WK_CHARGE, x));
                break;
            case 12101005:
            case 22121001:
                statups.Add(new BuffStatValue(MapleBuffStat.ELEMENT_RESET, x));
                break;
            case 3121008:
                statups.Add(new BuffStatValue(MapleBuffStat.CONCENTRATE, x));
                break;
            case 5110001:
            case 15100004:
                statups.Add(new BuffStatValue(MapleBuffStat.ENERGY_CHARGE, 0));
                break;
            case 1101005:
            case 1101004:
            case 1201005:
            case 1201004:
            case 1301005:
            case 1301004:
            case 3101002:
            case 3201002:
            case 4101003:
            case 4201002:
            case 2111005:
            case 2211005:
            case 5101006:
            case 5201003:
            case 11101001:
            case 12101004:
            case 13101001:
            case 14101002:
            case 15101002:
            case 21001003:
                statups.Add(new BuffStatValue(MapleBuffStat.BOOSTER, x));
                break;
            case 5121009:
            case 15111005:
                statups.Add(new BuffStatValue(MapleBuffStat.SPEED_INFUSION, x));
                break;
            case 5001005:
            case 15001003:
                statups.Add(new BuffStatValue(MapleBuffStat.DASH_SPEED, x));
                statups.Add(new BuffStatValue(MapleBuffStat.DASH_JUMP, y));
                break;
            case 1101007:
            case 1201007:
                statups.Add(new BuffStatValue(MapleBuffStat.POWERGUARD, x));
                break;
            case 1301007:
            case 9001008:
                statups.Add(new BuffStatValue(MapleBuffStat.MAXHP, x));
                statups.Add(new BuffStatValue(MapleBuffStat.MAXMP, y));
                break;
            case 1001:
            case 10001001:
            case 20001001:
                statups.Add(new BuffStatValue(MapleBuffStat.RECOVERY, x));
                break;
            case 1111002:
            case 11111001:
                statups.Add(new BuffStatValue(MapleBuffStat.COMBO, 1));
                break;
            case 21120007:
                statups.Add(new BuffStatValue(MapleBuffStat.COMBO_BARRIER, x));
                break;
            case 5211006:
            case 5220011:
                statups.Add(new BuffStatValue(MapleBuffStat.HOMING_BEACON, x));
                break;
            case 1011:
            case 10001011:
            case 20001011:
                statups.Add(new BuffStatValue(MapleBuffStat.BERSERK_FURY, 1));
                break;
            case 1010:
            case 10001010:
            case 20001010:
                statups.Add(new BuffStatValue(MapleBuffStat.DIVINE_BODY, 1));
                break;
            case 1311006:
                statups.Add(new BuffStatValue(MapleBuffStat.DRAGON_ROAR, y));
                break;
            case 1311008:
                statups.Add(new BuffStatValue(MapleBuffStat.DRAGONBLOOD, x));
                break;
            case 1121000:
            case 1221000:
            case 1321000:
            case 2121000:
            case 2221000:
            case 2321000:
            case 3121000:
            case 3221000:
            case 4121000:
            case 4221000:
            case 5121000:
            case 5221000:
            case 21121000:
                statups.Add(new BuffStatValue(MapleBuffStat.MAPLE_WARRIOR, x));
                break;
            case 15111006:
                statups.Add(new BuffStatValue(MapleBuffStat.SPARK, x));
                break;
            case 3121002:
            case 3221002:
                statups.Add(new BuffStatValue(MapleBuffStat.SHARP_EYES, x << 8 | y));
                break;
            case 21111001:
                statups.Add(new BuffStatValue(MapleBuffStat.SMART_KNOCKBACK, x));
                break;
            case 21101003:
                statups.Add(new BuffStatValue(MapleBuffStat.BODY_PRESSURE, x));
                break;
            case 21100005:
                statups.Add(new BuffStatValue(MapleBuffStat.COMBO_DRAIN, x));
                break;
            case 4341006:
            case 3111002:
            case 3211002:
            case 13111004:
            case 5211001:
            case 5220002:
                statups.Add(new BuffStatValue(MapleBuffStat.PUPPET, 1));
                break;
            case 3211005:
            case 3111005:
            case 3221005:
            case 2121005:
            case 2311006:
            case 3121006:
            case 2221005:
            case 2321003:
            case 1321007:
            case 5211002:
            case 11001004:
            case 12001004:
            case 12111004:
            case 13001004:
            case 14001005:
            case 15001004:
                statups.Add(new BuffStatValue(MapleBuffStat.SUMMON, 1));
                break;
            case 2311003:
            case 9001002:
                statups.Add(new BuffStatValue(MapleBuffStat.HOLY_SYMBOL, x));
                break;
            case 4121006:
                statups.Add(new BuffStatValue(MapleBuffStat.SPIRIT_CLAW, 0));
                break;
            case 2121004:
            case 2221004:
            case 2321004:
                statups.Add(new BuffStatValue(MapleBuffStat.INFINITY, x));
                break;
            case 1121002:
            case 1221002:
            case 1321002:
            case 21121003:
                statups.Add(new BuffStatValue(MapleBuffStat.STANCE, prop));
                break;
            case 1005:
            case 10001005:
            case 20001005:
                statups.Add(new BuffStatValue(MapleBuffStat.ECHO_OF_HERO, x));
                break;
            case 2121002:
            case 2221002:
            case 2321002:
                statups.Add(new BuffStatValue(MapleBuffStat.MANA_REFLECTION, 1));
                break;
            case 2321005:
                statups.Add(new BuffStatValue(MapleBuffStat.HOLY_SHIELD, x));
                break;
            case 3121007:
                statups.Add(new BuffStatValue(MapleBuffStat.HAMSTRING, x));
                break;
            case 3221006:
                statups.Add(new BuffStatValue(MapleBuffStat.BLIND, x));
                break;
        }
    }

    private static bool IsMorphSkill(int skillId, int morphId)
        => morphId > 0 || skillId is 5111005 or 5121003 or 15111002 or 13111005;

    private static bool IsFinalAttackSkill(int skillId)
        => skillId is 1100002 or 1100003 or 1200002 or 1200003 or 1300002 or 1300003
            or 3100001 or 3200001 or 11101002 or 13101002 or 21120002;

    private static bool IsEnergyCharge(int skillId)
        => skillId is 5110001 or 15100004;

    private static bool IsMonsterRiding(int skillId)
        => skillId is 1004 or 10001004 or 20001004 or 20011004 or 30001004;

    private static bool IsFieldObjectSkill(int skillId)
        => skillId is 2311002 or 8001 or 10008001 or 20008001 or 20018001 or 30008001
            or 4341006 or 3111002 or 3211002 or 13111004 or 5211001 or 5220002
            or 3211005 or 3111005 or 3221005 or 2121005 or 2311006 or 3121006
            or 2221005 or 2321003 or 1321007 or 5211002 or 11001004 or 12001004
            or 12111004 or 13001004 or 14001005 or 15001004;

    private static void AddIfNotZero(List<BuffStatValue> statups, MapleBuffStat stat, int value)
    {
        if (value != 0)
        {
            statups.Add(new BuffStatValue(stat, value));
        }
    }

    private static short ReadShort(IDataNode node, string key)
        => (short)Math.Clamp(GetInt(node, key, 0), short.MinValue, short.MaxValue);

    private static int GetInt(IDataNode? node, string key, int defaultValue)
    {
        var child = node?[key];
        return child?.Value switch
        {
            int v => v,
            short v => v,
            long v when v <= int.MaxValue && v >= int.MinValue => (int)v,
            byte v => v,
            sbyte v => v,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    private static string GetString(IDataNode? node, string key, string defaultValue = "")
    {
        var child = node?[key];
        return child?.Value is string s ? s : defaultValue;
    }

    private static int ParseInt(string value, int defaultValue)
        => int.TryParse(value, out var parsed) ? parsed : defaultValue;

    private sealed record CachedSkill(MapleSkill? Skill);
}
