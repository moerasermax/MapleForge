using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// v113 怪物異常狀態編碼（P110，對照 Java <c>MonsterStatus</c> 的 i/pos/order/isDefault 與
/// <c>MobPacket.writeMaskFromList</c>／<c>EncodeTemporary</c>／<c>ProcessStatSet</c>）。
/// </summary>
internal static class V113MonsterStatusCodec
{
    public const short ApplyMonsterStatusOp = unchecked((short)0xEB);  // send.properties APPLY_MONSTER_STATUS
    public const short CancelMonsterStatusOp = unchecked((short)0xEC); // send.properties CANCEL_MONSTER_STATUS

    /// <summary>Java 建構子帶第二參數的 order 覆寫；其餘 order = pos（i&lt;32 為 3，否則 2）。</summary>
    private static readonly IReadOnlyDictionary<MonsterStatus, int> OrderOverrides = new Dictionary<MonsterStatus, int>
    {
        [MonsterStatus.Doom] = 18,
        [MonsterStatus.ShadowWeb] = 19,
        [MonsterStatus.WeaponImmunity] = 16,
        [MonsterStatus.MagicImmunity] = 17,
        [MonsterStatus.Showdown] = 32,
        [MonsterStatus.DamageImmunity] = 20,
        [MonsterStatus.NinjaAmbush] = 21,
        [MonsterStatus.VenomousWeapon] = 22,
        [MonsterStatus.Blind] = 23,
        [MonsterStatus.SealSkill] = 24,
        [MonsterStatus.Empty] = 33,
        [MonsterStatus.Hypnotize] = 25,
        [MonsterStatus.WeaponDamageReflect] = 26,
        [MonsterStatus.MagicDamageReflect] = 27,
        [MonsterStatus.Summon] = 34,
        [MonsterStatus.Mbs32] = 28,
        [MonsterStatus.Neutralise] = 29,
        [MonsterStatus.Imprint] = 30,
        [MonsterStatus.MonsterBomb] = 31,
    };

    public static int Position(MonsterStatus status) => 3 - (int)status / 32;

    public static int Bit(MonsterStatus status) => 1 << ((int)status % 32);

    public static int Order(MonsterStatus status)
        => OrderOverrides.TryGetValue(status, out var order) ? order : Position(status);

    public static bool IsDefault(MonsterStatus status) => status == MonsterStatus.Empty;

    /// <summary>Java <c>writeMaskFromList</c>：4 個 int，mask[pos] |= bit。</summary>
    public static void WriteMask(PacketWriter w, IEnumerable<MonsterStatus> statuses)
    {
        var mask = new int[4];
        foreach (var status in statuses)
        {
            mask[Position(status)] |= Bit(status);
        }

        foreach (var value in mask)
        {
            w.WriteInt(value);
        }
    }

    /// <summary>Java <c>EncodeTemporary</c>：遮罩 → 依 order（同值保持列舉序，同 Java EnumMap＋穩定排序）逐一寫 payload。</summary>
    public static void EncodeTemporary(PacketWriter w, IReadOnlyCollection<MonsterStatusEffect> effects)
    {
        WriteMask(w, effects.Select(static e => e.Status));

        var ordered = effects
            .OrderBy(static e => Order(e.Status))
            .ThenBy(static e => (int)e.Status)
            .ToArray();
        foreach (var effect in ordered)
        {
            if (effect.Status == MonsterStatus.DamagedElemAttr)
            {
                continue;
            }

            if (effect.Status == MonsterStatus.Empty)
            {
                w.WriteInt(0);
                continue;
            }

            w.WriteShort((short)effect.Value);
            if (effect.MobSkill is { } mobSkill)
            {
                w.WriteShort((short)mobSkill.SkillId);
                w.WriteShort((short)mobSkill.Level);
            }
            else if (effect.SkillId > 0)
            {
                w.WriteInt(effect.SkillId);
            }

            w.WriteShort((short)(IsDefault(effect.Status) ? 0 : 1));
        }

        var statuses = ordered.Select(static e => e.Status).ToHashSet();
        if (statuses.Contains(MonsterStatus.WeaponDamageReflect))
        {
            w.WriteInt(0);
        }

        if (statuses.Contains(MonsterStatus.MagicDamageReflect))
        {
            w.WriteInt(0);
        }

        if (statuses.Contains(MonsterStatus.WeaponDamageReflect))
        {
            w.WriteInt(0);
        }

        if (statuses.Contains(MonsterStatus.Summon))
        {
            w.WriteByte(0);
            w.WriteByte(0);
        }
    }

    /// <summary>
    /// 出生封包的狀態區（Java <c>addMonsterStatus</c>：無狀態時先 <c>addEmpty</c>，EMPTY 自此常駐）。
    /// MapleForge 不把 EMPTY 存進 Mob，編碼時一律補上。
    /// </summary>
    public static void WriteSpawnStatus(PacketWriter w, Mob mob)
    {
        var effects = mob.Statuses.Values
            .Append(new MonsterStatusEffect(MonsterStatus.Empty, 0, 0, null, null))
            .ToArray();
        EncodeTemporary(w, effects);
    }

    /// <summary>Java <c>applyMonsterStatus</c>（單一或毒類清單）：EncodeTemporary + short 2 + byte 1 + byte 1（unverified）。</summary>
    public static byte[] ApplyMonsterStatus(int objectId, IReadOnlyCollection<MonsterStatusEffect> effects)
    {
        var w = new PacketWriter();
        w.WriteShort(ApplyMonsterStatusOp);
        w.WriteInt(objectId);
        EncodeTemporary(w, effects);
        w.WriteShort(2);
        w.WriteByte(1);
        w.WriteByte(1);
        return w.ToArray();
    }

    /// <summary>Java <c>cancelMonsterStatus</c>：遮罩 + int 0（unverified）。</summary>
    public static byte[] CancelMonsterStatus(int objectId, IEnumerable<MonsterStatus> statuses)
    {
        var w = new PacketWriter();
        w.WriteShort(CancelMonsterStatusOp);
        w.WriteInt(objectId);
        WriteMask(w, statuses);
        w.WriteInt(0);
        return w.ToArray();
    }
}
