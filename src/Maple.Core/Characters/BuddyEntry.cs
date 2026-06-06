namespace Maple.Core.Characters;

/// <summary>單一好友項目；欄位對齊舊 Java BuddyEntry 的持久資料與執行期 channel 狀態。</summary>
public sealed class BuddyEntry
{
    public int CharacterId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Group { get; set; } = BuddyList.DefaultGroup;

    /// <summary>server-side channel，離線為 -1；送 v113 buddy list 時再轉成 client channel index。</summary>
    public int Channel { get; set; } = -1;

    /// <summary>Java BuddyEntry.visible：true=已確認好友，false=尚未接受/不可見。</summary>
    public bool Visible { get; set; }

    /// <summary>此筆 false-visible entry 是否為對方送來、需要提示自己的待接受請求。</summary>
    public bool PendingRequest { get; set; }

    /// <summary>執行期去重，避免同一段連線內反覆彈出同一筆 pending request。</summary>
    public bool RequestPrompted { get; set; }

    public BuddyEntry Clone()
    {
        return new BuddyEntry
        {
            CharacterId = CharacterId,
            Name = Name,
            Group = Group,
            Channel = Channel,
            Visible = Visible,
            PendingRequest = PendingRequest,
            RequestPrompted = RequestPrompted,
        };
    }
}
