namespace Maple.Adapters.V113.Channel;

/// <summary>v113 Channel 接收 opcode。</summary>
internal static class V113ChannelRecvOp
{
    public const short PlayerLoggedIn = 0x07;
    public const short MovePlayer = 0x21;
    public const short CloseRangeAttack = 0x25;
    public const short RangedAttack = 0x26;
    public const short MagicAttack = 0x27;
    public const short GeneralChat = 0x2A;   // 一般地圖聊天
    public const short DistributeAp = 0x51;
    public const short HealOverTime = 0x53;
    public const short DistributeSp = 0x54;
    public const short QuestAction = 0x65;
    public const short PartyOperation = 0x74;
    public const short BuddyListModify = 0x7A;
    public const short UpdateQuest = 0x10B;
    public const short UseItemQuest = 0x10D;
    public const short NpcTalk = 0x33;       // 點 NPC → 啟動對話腳本
    public const short NpcTalkMore = 0x35;   // 對話中回應（next/prev/yes-no/選單/數字）
    public const short NpcShop = 0x36;
    public const short Storage = 0x37;
    public const short ItemMove = 0x41;      // 背包格內移動 / 穿脫裝 / 丟棄（dst=0）
    public const short Pong = 0x0E;
}

/// <summary>v113 Channel 送出 opcode。</summary>
internal static class V113ChannelSendOp
{
    public const short SetField = 0x7B;   // WARP_TO_MAP / initial login / map change
    public const short ChatText = unchecked((short)0x9B);   // 地圖聊天泡泡
    public const short Ping = 0x09;
    public const short ModifyInventoryItem = 0x1B;   // 背包變更（新增/數量/移動/移除）
    public const short UpdateStats = 0x1D;
    public const short UpdateSkills = 0x22;
    public const short ShowStatusInfo = 0x25;
    public const short ShowQuestCompletion = 0x2E;
    public const short PartyOperation = 0x37;
    public const short BuddyList = 0x38;
    public const short SpawnNpc = unchecked((short)0xF9);              // SPAWN_NPC
    public const short RemoveNpc = unchecked((short)0xFA);            // REMOVE_NPC
    public const short SpawnNpcRequestController = unchecked((short)0xFB);  // SPAWN_NPC_REQUEST_CONTROLLER
    public const short CloseRangeAttack = unchecked((short)0xB2);
    public const short NpcTalk = 0x13C;   // NPC 對話框（getNPCTalk，2-byte opcode）
    public const short OpenNpcShop = 0x13D;
    public const short ConfirmShopTransaction = 0x13E;
    public const short OpenStorage = 0x141;
    public const short SpawnMonster = unchecked((short)0xE5);
    public const short KillMonster = unchecked((short)0xE6);
    public const short SpawnMonsterControl = unchecked((short)0xE7);
    public const short MoveMonster = unchecked((short)0xE8);
    public const short MoveMonsterResponse = unchecked((short)0xE9);
    public const short DamageMonster = unchecked((short)0xEF);
    public const short UpdatePartyMemberHp = unchecked((short)0xC2);
    public const short UpdateQuestInfo = unchecked((short)0xCC);
}
