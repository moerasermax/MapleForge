namespace Maple.Core.World;

/// <summary>玩家異常狀態（P126，對照 Java <c>client.MapleDisease</c>；遮罩位元屬協定細節，由 Adapters 決定）。</summary>
public enum PlayerDisease
{
    Potion,
    Shadow,
    Blind,
    Freeze,
    Slow,
    Morph,
    Seduce,
    Zombify,
    ReverseDirection,
    WeirdFlame,
    Stun,
    Poison,
    Seal,
    Darkness,
    Weaken,
    Curse,
}

/// <summary>生效中的異常狀態（Java <c>MapleDiseaseValueHolder</c>＋施放來源）。</summary>
public sealed record ActiveDisease(
    PlayerDisease Disease,
    int X,
    int SkillId,
    int Level,
    DateTimeOffset StartedAt,
    int DurationMilliseconds)
{
    /// <summary>Java <c>startTime + length &lt; now</c>（嚴格小於）。</summary>
    public bool IsExpired(DateTimeOffset now) => StartedAt.AddMilliseconds(DurationMilliseconds) < now;
}

public sealed partial class Player
{
    /// <summary>Java 神聖之盾（2321005）：擋 SEDUCE/STUN 以外的異常。</summary>
    public const int HolyShieldSkillId = 2321005;

    private readonly Dictionary<PlayerDisease, ActiveDisease> _diseases = new();

    public IReadOnlyList<ActiveDisease> Diseases
    {
        get
        {
            lock (_skillsGate)
            {
                return _diseases.Values.ToArray();
            }
        }
    }

    public bool HasDisease(PlayerDisease disease)
    {
        lock (_skillsGate)
        {
            return _diseases.ContainsKey(disease);
        }
    }

    /// <summary>
    /// 對照 Java <c>MapleCharacter.getDiseaseBuff</c>：尚未有此異常且同時異常數 &lt; 2 才給；SEDUCE/STUN 以外被神聖之盾擋下。
    /// 回傳是否真的給了（呼叫端據此送 giveDebuff）。GM 無敵狀態 MapleForge 尚無。
    /// </summary>
    public ActiveDisease? TryGiveDisease(PlayerDisease disease, int x, int durationMilliseconds, int skillId, int level, DateTimeOffset now)
    {
        lock (_skillsGate)
        {
            if (_diseases.ContainsKey(disease) || _diseases.Count >= 2)
            {
                return null;
            }

            if (disease is not (PlayerDisease.Seduce or PlayerDisease.Stun)
                && _activeBuffs.Values.Any(static b => b.SourceId == HolyShieldSkillId))
            {
                return null;
            }

            var active = new ActiveDisease(disease, x, skillId, level, now, durationMilliseconds);
            _diseases[disease] = active;
            return active;
        }
    }

    /// <summary>Java <c>dispelDebuff</c>：有才移除，回傳是否移除（呼叫端送 cancelDebuff）。</summary>
    public bool DispelDisease(PlayerDisease disease)
    {
        lock (_skillsGate)
        {
            return _diseases.Remove(disease);
        }
    }

    /// <summary>Java <c>World.handleCooldowns</c> 的異常到期（startTime + length &lt; now）。</summary>
    public IReadOnlyList<ActiveDisease> ExpireDiseases(DateTimeOffset now)
    {
        lock (_skillsGate)
        {
            var expired = _diseases.Values.Where(d => d.IsExpired(now)).ToArray();
            foreach (var disease in expired)
            {
                _diseases.Remove(disease.Disease);
            }

            return expired;
        }
    }

    /// <summary>Java <c>MapleDisease.getByMobSkill</c>。</summary>
    public static PlayerDisease? DiseaseFromMobSkill(int mobSkillId) => mobSkillId switch
    {
        120 => PlayerDisease.Seal,
        121 => PlayerDisease.Darkness,
        122 => PlayerDisease.Weaken,
        123 => PlayerDisease.Stun,
        124 => PlayerDisease.Curse,
        125 => PlayerDisease.Poison,
        126 => PlayerDisease.Slow,
        128 => PlayerDisease.Seduce,
        132 => PlayerDisease.ReverseDirection,
        133 => PlayerDisease.Zombify,
        134 => PlayerDisease.Potion,
        135 => PlayerDisease.Shadow,
        136 => PlayerDisease.Blind,
        137 => PlayerDisease.Freeze,
        _ => null,
    };
}
