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
}
