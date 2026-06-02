namespace Maple.Adapters.V113.Channel;

/// <summary>v113 Channel 接收 opcode。</summary>
internal static class V113ChannelRecvOp
{
    public const short PlayerLoggedIn = 0x07;
    public const short MovePlayer = 0x21;
    public const short GeneralChat = 0x2A;   // 一般地圖聊天
    public const short NpcTalk = 0x33;       // 點 NPC → 啟動對話腳本
    public const short NpcTalkMore = 0x35;   // 對話中回應（next/prev/yes-no/選單/數字）
    public const short Pong = 0x0E;
}

/// <summary>v113 Channel 送出 opcode。</summary>
internal static class V113ChannelSendOp
{
    public const short SetField = 0x7B;   // WARP_TO_MAP / initial login / map change
    public const short ChatText = unchecked((short)0x9B);   // 地圖聊天泡泡
    public const short Ping = 0x09;
    public const short SpawnNpc = unchecked((short)0xF9);              // SPAWN_NPC
    public const short RemoveNpc = unchecked((short)0xFA);            // REMOVE_NPC
    public const short SpawnNpcRequestController = unchecked((short)0xFB);  // SPAWN_NPC_REQUEST_CONTROLLER
    public const short NpcTalk = 0x13C;   // NPC 對話框（getNPCTalk，2-byte opcode）
}
