namespace Maple.Core.World;

/// <summary>
/// 地圖霧（P131，對照 Java <c>MapleMist</c>）：怪物毒霧或玩家技能霧（劇毒霧/煙霧彈/恢復光環）。
/// <paramref name="OwnerId"/>：怪物霧為怪物模板 id（Java <c>mob.getId()</c>）、玩家霧為角色 id。
/// <paramref name="PoisonMode"/>：Java <c>isPoisonMist</c>（0 無效果、1 對怪物上毒、2 回 MP）。
/// 範圍為 (左, 上, 寬, 高)，對應 <c>java.awt.Rectangle</c>。
/// </summary>
public sealed class Mist : IFieldObject
{
    public Mist(
        int objectId,
        int ownerId,
        int skillId,
        int skillLevel,
        bool isMobMist,
        int poisonMode,
        short skillDelay,
        (int X, int Y, int Width, int Height) box,
        DateTimeOffset expiresAt)
    {
        ObjectId = objectId;
        OwnerId = ownerId;
        SkillId = skillId;
        SkillLevel = skillLevel;
        IsMobMist = isMobMist;
        PoisonMode = poisonMode;
        SkillDelay = skillDelay;
        Box = box;
        ExpiresAt = expiresAt;
    }

    public int ObjectId { get; }

    public int OwnerId { get; }

    public int SkillId { get; }

    public int SkillLevel { get; }

    public bool IsMobMist { get; }

    public int PoisonMode { get; }

    public short SkillDelay { get; }

    public (int X, int Y, int Width, int Height) Box { get; }

    public DateTimeOffset ExpiresAt { get; }

    public FieldObjectType Type => FieldObjectType.Mist;

    /// <summary>Java <c>getPosition</c> = 範圍左上角。</summary>
    public Position Position => new((short)Box.X, (short)Box.Y, 0, 0);
}
