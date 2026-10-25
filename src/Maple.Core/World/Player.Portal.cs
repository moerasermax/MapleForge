namespace Maple.Core.World;

public sealed partial class Player
{
    /// <summary>P138：Java <c>lastUsingPortal</c>（內部傳送門使用時間；null = 0）。</summary>
    public DateTimeOffset? LastUsingPortalAt { get; private set; }

    /// <summary>Java <c>updateUsingPortal(true)</c>。</summary>
    public void MarkUsingPortal(DateTimeOffset now) => LastUsingPortalAt = now;

    /// <summary>Java <c>checkUsingPortal</c>：使用過內部傳送門且未滿 2 秒。</summary>
    public bool IsUsingPortal(DateTimeOffset now) => LastUsingPortalAt is { } at && at.AddSeconds(2) >= now;
}
