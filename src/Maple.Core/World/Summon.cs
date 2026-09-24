namespace Maple.Core.World;

/// <summary>地圖上的召喚獸執行期模型。</summary>
public sealed class Summon : IFieldObject
{
    public int ObjectId { get; }

    public int SkillId { get; }

    public byte SkillLevel { get; }

    public int OwnerId { get; }

    public short Hp { get; private set; }

    public SummonMovementType MovementType { get; }

    public Position Position { get; private set; }

    public FieldObjectType Type => FieldObjectType.Summon;

    public bool IsPuppet => SkillId is 3111002 or 3211002 or 13111004 or 4341006 or 33111003;

    public Summon(
        int objectId,
        int skillId,
        byte skillLevel,
        int ownerId,
        short hp,
        SummonMovementType movementType,
        Position position)
    {
        ObjectId = objectId;
        SkillId = skillId;
        SkillLevel = skillLevel;
        OwnerId = ownerId;
        Hp = hp;
        MovementType = movementType;
        Position = position;
    }

    /// <summary>扣除召喚獸 HP；回傳實際扣除量。</summary>
    public short TakeDamage(int damage)
    {
        if (damage <= 0 || Hp <= 0)
        {
            return 0;
        }

        var applied = (short)Math.Min(damage, Hp);
        Hp = (short)(Hp - applied);
        return applied;
    }

    public void MoveTo(Position position) => Position = position;

    /// <summary>
    /// P081：對照 Java <c>MapleStatEffect.getSummonMovementType</c>——哪些技能會產生召喚獸、用哪種移動型態；
    /// 非召喚技能回 null。
    /// </summary>
    public static SummonMovementType? GetMovementType(int skillId)
        => skillId switch
        {
            3211002 or 3111002 or 33111003 or 13111004 or 5211001 or 5220002 or 4341006
                or 35111002 or 35111005 or 35111004 or 35121009 or 35121011 => SummonMovementType.Stationary,
            3211005 or 3111005 or 33111005 or 2311006 or 3221005 or 3121006 => SummonMovementType.CircleFollow,
            5211002 => SummonMovementType.CircleStationary,
            32111006 => SummonMovementType.WalkStationary,
            1321007 or 2121005 or 2221005 or 2321003 or 12111004 or 11001004 or 12001004 or 13001004
                or 14001005 or 15001004 or 35111001 or 35111010 or 35111009 => SummonMovementType.Follow,
            _ => null,
        };
}
