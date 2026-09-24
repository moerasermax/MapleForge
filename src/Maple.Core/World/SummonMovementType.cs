namespace Maple.Core.World;

/// <summary>
/// 召喚獸移動型態。Core 只保存語義值，不知道 v113 opcode／線上數值（線上值由 Adapter 對照 Java
/// <c>server.maps.SummonMovementType</c> 映射，見 P080）。
/// </summary>
public enum SummonMovementType
{
    /// <summary>原地不動（章魚、傀儡）。</summary>
    Stationary = 1,
    /// <summary>跟隨主人（四轉法師召喚、暗黑之魂）。</summary>
    Follow = 2,
    /// <summary>原地來回走動（死神）。</summary>
    WalkStationary = 3,
    /// <summary>繞著主人飛（弓手鳥類、聖龍）。</summary>
    CircleFollow = 4,
    /// <summary>原地盤旋（海鷗）。</summary>
    CircleStationary = 5,
}
