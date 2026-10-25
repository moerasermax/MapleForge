namespace Maple.Core.Items;

/// <param name="Cures">P127：解除的異常（Java spec poison/seal/darkness/weakness/curse → cureDebuffs）。</param>
public sealed record ItemEffect(int ItemId, int Hp, int Mp, int HpRate, int MpRate, IReadOnlyList<World.PlayerDisease>? Cures = null);

public interface IItemEffectCatalog
{
    ItemEffect? GetEffect(int itemId);

    /// <summary>
    /// P164：消耗品的持續 buff（Java <c>loadItemEffectFromData</c> 的 overTime/statups/morph）。
    /// <see cref="Skills.MapleStatEffect.SourceId"/> 為 <c>-itemId</c>（Java giveBuff 的 <c>-sourceid</c>，避免與技能 ID 撞鍵）。
    /// 無 buff 回 null。
    /// </summary>
    Skills.MapleStatEffect? GetBuffEffect(int itemId) => null;
}
