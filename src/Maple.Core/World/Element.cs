namespace Maple.Core.World;

/// <summary>屬性（P114，對照 Java <c>server.life.Element</c>）。</summary>
public enum Element
{
    Neutral,
    Physical,
    Fire,
    Ice,
    Lightning,
    Poison,
    Holy,
    Darkness,
}

/// <summary>屬性抗性（對照 Java <c>ElementalEffectiveness</c>：WZ 數字 1 免疫、2 抗性、3 弱點）。</summary>
public enum ElementalEffectiveness
{
    Normal,
    Immune,
    Strong,
    Weak,
}

/// <summary>WZ <c>elemAttr</c> 字串解析（對照 Java <c>Element.getFromChar</c>／<c>MapleLifeFactory.decodeElementalString</c>）。</summary>
public static class ElementCodec
{
    public static Element? FromChar(char c) => char.ToUpperInvariant(c) switch
    {
        'F' => Element.Fire,
        'I' => Element.Ice,
        'L' => Element.Lightning,
        'S' => Element.Poison,
        'H' => Element.Holy,
        'P' => Element.Physical,
        'D' => Element.Darkness,
        _ => null,
    };

    /// <summary>
    /// 兩字元一組（屬性字元＋抗性數字）。Java 遇到未知字元會丟例外中止載入；MapleForge 略過該組。
    /// </summary>
    public static IReadOnlyDictionary<Element, ElementalEffectiveness> DecodeResistances(string? elemAttr)
    {
        var map = new Dictionary<Element, ElementalEffectiveness>();
        if (string.IsNullOrEmpty(elemAttr))
        {
            return map;
        }

        for (var i = 0; i + 1 < elemAttr.Length; i += 2)
        {
            var effectiveness = elemAttr[i + 1] switch
            {
                '1' => ElementalEffectiveness.Immune,
                '2' => ElementalEffectiveness.Strong,
                '3' => ElementalEffectiveness.Weak,
                _ => (ElementalEffectiveness?)null,
            };
            if (FromChar(elemAttr[i]) is { } element && effectiveness is { } value)
            {
                map[element] = value;
            }
        }

        return map;
    }
}
