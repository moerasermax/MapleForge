---
編號: 2026-06-02_09
標題: 移植一般地圖聊天（Java ChatHandler.GeneralChat）
類型: 重構
狀態: ✅ 完成（一般聊天；好友/隊伍/公會/密語/messenger 待社交階段）
建立: 2026-06-02 16:40
更新: 2026-06-02 16:45
關聯里程碑: 移植路線圖 ①in-game 基礎
關聯記憶: v113-pivot-port-from-java
關聯commit: afe6e28
---

## 🎯 目標
> 參照 Java `ChatHandler.GeneralChat` 移植「一般地圖聊天」：c2s GENERAL_CHAT(0x2A) → s2c CHATTEXT(0x9B) 送自己+廣播同地圖。完成判準：封包結構單元測試綠 + 接進 channel handler。

## 📜 執行歷程
- **16:40** 讀 Java ChatHandler + getChatText(0x9B:[int cid][byte whiteBG][maple text][byte show]) + opcode(GENERAL_CHAT=0x2A)。
- **16:45 ✅ 移植**：①V113ChannelOpcodes 加 GeneralChat=0x2A / ChatText=0x9B ②`V113MapPackets.ChatText` 封包建構器 ③handler 加 `HandleGeneralChatAsync`(讀 text+show→送自己+廣播 others;text>=80 擋,對照 Java)。單元測試 ChannelChatPacketTests 綠,Adapters.V113 33→34。社交類聊天(好友/隊伍/公會/密語/messenger)＝Others/Whisper/Messenger,待社交階段(需 party/guild/buddy/world server)。

## ⏯️ 接手點（★崩潰救命行★）
> ✅ 一般聊天完成+測+commit。in-game 基礎續：地圖物件同步(NPC/portal spawn)、多玩家 spawn 位置。里程碑時真客戶端 smoke(打字→泡泡)。

## ✅ 結果與結論
> 達標：一般地圖聊天 c2s→s2c 移植+測。下一單元見路線圖。

## 🔗 產出
> `V113ChannelOpcodes`(GeneralChat/ChatText)、`V113MapPackets.ChatText`、handler `HandleGeneralChatAsync`、`ChannelChatPacketTests`。commit 待填。
