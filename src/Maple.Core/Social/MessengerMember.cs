namespace Maple.Core.Social;

public sealed record MessengerMember(
    int CharacterId,
    string Name,
    int ChannelIndex,
    int Position);
