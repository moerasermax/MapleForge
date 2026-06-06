using Maple.Core.Maps;

namespace Maple.Core.World;

/// <summary>怪物模板數值（從 Mob.wz info 載入；Core 不知道 WZ 格式）。</summary>
public sealed record MobStats(
    int MonsterId,
    long MaxHp,
    int MaxMp,
    short Level,
    int Exp,
    bool Boss = false,
    bool Mobile = false,
    bool Friendly = false,
    byte HpDisplayType = 3);

/// <summary>單次怪物傷害套用結果。</summary>
public sealed record MobDamageResult(
    int ObjectId,
    int MonsterId,
    long RequestedDamage,
    long AppliedDamage,
    long RemainingHp,
    bool Killed);

/// <summary>
/// 執行期怪物（地圖物件）：持有靜態出生點、模板數值、場上 objectId 與可變 HP/MP。
/// 對照舊 OdinMS <c>MapleMonster</c>，但只保留戰鬥 MVP 所需生命週期。
/// </summary>
public sealed class Mob : IFieldObject
{
    public const byte DefaultStance = 5;

    public MapMonster Definition { get; }

    public MobStats Stats { get; }

    public int ObjectId { get; }

    public Position Position { get; private set; }

    public FieldObjectType Type => FieldObjectType.Monster;

    public long Hp { get; private set; }

    public int Mp { get; private set; }

    public bool IsAlive => Hp > 0;

    public short OriginFoothold { get; }

    public sbyte CarnivalTeam => Definition.Team;

    public Mob(MapMonster definition, MobStats stats, int objectId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(stats);
        if (stats.MonsterId != definition.MonsterId)
        {
            throw new ArgumentException("MobStats.MonsterId must match MapMonster.MonsterId.", nameof(stats));
        }

        Definition = definition;
        Stats = stats;
        ObjectId = objectId;
        OriginFoothold = (short)definition.Fh;
        Position = new Position((short)definition.X, (short)definition.Y, DefaultStance, (short)definition.Fh);
        Hp = Math.Max(1, stats.MaxHp);
        Mp = Math.Max(0, stats.MaxMp);
    }

    /// <summary>套用客戶端攻擊傷害；扣血量會夾在 [0, CurrentHp]。</summary>
    public MobDamageResult TakeDamage(long damage)
    {
        if (damage <= 0 || !IsAlive)
        {
            return new MobDamageResult(ObjectId, Definition.MonsterId, damage, 0, Hp, false);
        }

        var applied = Math.Min(damage, Hp);
        Hp -= applied;
        return new MobDamageResult(ObjectId, Definition.MonsterId, damage, applied, Hp, Hp == 0);
    }

    /// <summary>套用怪物移動最終位置（MONSTER_MOVE 後續接線用）。</summary>
    public void MoveTo(Position position) => Position = position;
}
