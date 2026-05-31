namespace Maple.Core.Characters;

/// <summary>角色的基礎數值（對照舊 PlayerStats.connectData()）。</summary>
public sealed class CharacterStats
{
    public short Str { get; set; } = 12;
    public short Dex { get; set; } = 5;
    public short Int { get; set; } = 4;
    public short Luk { get; set; } = 4;
    public short Hp { get; set; } = 50;
    public short MaxHp { get; set; } = 50;
    public short Mp { get; set; } = 5;
    public short MaxMp { get; set; } = 5;
}
