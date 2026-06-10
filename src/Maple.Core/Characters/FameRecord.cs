namespace Maple.Core.Characters;

/// <summary>玩家給予人氣的節流紀錄；跟隨 Character 文件一起持久化。</summary>
public sealed class FameRecord
{
    public int TargetCharacterId { get; set; }

    public long GivenAtUnixMillis { get; set; }
}
