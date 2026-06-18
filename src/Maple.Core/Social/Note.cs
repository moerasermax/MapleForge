namespace Maple.Core.Social;

public sealed record Note
{
    public int Id { get; set; }

    public string SenderName { get; set; } = string.Empty;

    public string ReceiverName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int Fame { get; set; }

    public long Timestamp { get; set; }

    public bool Read { get; set; }
}
