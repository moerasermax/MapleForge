namespace Maple.Core.Characters;

/// <summary>一件裝備：位置（負數為穿戴欄）＋道具 ID。</summary>
public sealed class EquipEntry
{
    public short Position { get; set; }
    public int ItemId { get; set; }
}
