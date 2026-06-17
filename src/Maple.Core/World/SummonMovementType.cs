namespace Maple.Core.World;

/// <summary>召喚獸移動型態。Core 只保存語義值，不知道 v113 opcode。</summary>
public enum SummonMovementType
{
    Stationary = 1,
    Follow = 2,
    CircleFollow = 4,
}
