---
編號: 2026-09-06_61
標題: P059 — 公會成員升級同步（GuildMemberLevelJobUpdate + 同盟轉發，零呼叫者封包補完整）
類型: 移植
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P059
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

延續「零呼叫者封包掃描」手法找到的候選：`V113GuildPackets.GuildMemberLevelJobUpdate`
（公會成員等級/職業變動廣播）跟 `V113AlliancePackets.UpdateAllianceMember`（同盟轉發）都
存在但零呼叫者。對照 Java `MapleGuild.memberLevelJobUpdate`：玩家在公會裡升級時，要同步
公會登記表快取的等級欄位、依公式加公會經驗值（GP）、廣播給全公會（含自己）、若公會屬於
同盟還要轉發給同盟裡其他公會的線上成員。完成判準：至少覆蓋「打怪經驗值升級」這個目前
最主要的升級途徑，並忠實對照 Java 的 GP 公式與廣播範圍。

## 📋 背景與查證

- 用「零呼叫者封包掃描」（P045/P046 手法：先排除同檔案 dispatcher pattern、C# method-group
  參照、多 class 檔案誤判）掃過 `V113*Packets.cs` 的 `public static byte[]` 方法，找到
  `GuildMemberLevelJobUpdate`/`UpdateAllianceMember` 等 8 個候選；逐一追 Java 呼叫鏈後，
  多數（Door/Portal 系列需要 `SPECIAL_MOVE` 傳送門完整子系統、`RemovePet` 需要尚未存在的
  寵物飢餓值週期性衰減計時器子系統）確認需要更大的前置子系統，只有這組公會/同盟等級同步
  是「資料跟廣播管線都已就位，純粹缺一段呼叫」的缺口，範圍明確可在一個 P 內完成。
- 對照 Java `MapleGuild.memberLevelJobUpdate`：更新成員快取的 `level`/`jobId`，等級提升時
  依 `(newLevel-oldLevel)*newLevel/10` 公式呼叫 `gainGP`；`old_level != mgc.getLevel()`才送
  `sendLevelup`／`old_job != mgc.getJobId()`才送 `sendJobup`（純聊天視覺通知，機制跟本次無關，
  刻意不動）；`broadcast(guildMemberLevelJobUpdate(mgc))` 無條件對全公會（含自己）廣播；若
  `allianceid > 0`，用 `World.Alliance.sendGuild(updateAlliance(mgc, allianceid), id, allianceid)`
  以 `guildId` 當排除鍵通知同盟裡其他公會的線上成員（來源公會已經在上面廣播過，不重複）。
- 查證 MapleForge 現況：`Character.Job`（職業轉職）目前沒有任何寫入點——轉職功能本身尚未
  實作，所以 Java 邏輯裡「職業變動」這一半目前恆為 no-op（`jobId` 永遠等於原值），刻意保留
  介面參數但不強求現在就有轉職觸發點，等轉職功能落地後自然會生效，不需要額外改動。
- 查證升級來源：`Player.Stats.GainExperience`/`ApplyLevelUp` 是 Core 層純函式，實際呼叫端
  只有兩處——`SendMobKillRewardsAsync`（打怪經驗值，主要途徑）跟 `V113BuffItemHandler`
  （藥水/道具直接領經驗值，較少見）。這次只接打怪這條主要路徑，道具領經驗值升級路徑
  明確記下留給後續 P-phase，避免同一個 P 裡擴大範圍（見任務歷程「結果與結論」）。

## 🔧 實作內容

- **`Maple.Core`**（`Guild.cs`）：新增 `TryUpdateMemberLevelJob(characterId, level, jobId, out changed)`
  ——對照 Java 邏輯：找不到成員回 false；找到就更新 `Level`/`JobId`，等級提升才呼叫既有的
  `GainGuildPoints` 依公式加 GP。
- **`Maple.Application`**（`GuildService.cs`）：
  - `GuildUpdateKind` 新增 `MemberLevelJobChanged`。
  - `IGuildRegistry`/`InMemoryGuildRegistry` 新增 `UpdateMemberLevelJobAsync`，沿用
    `ChangeRankAsync` 既有的「鎖內找公會→呼叫 Core 層 mutation→持久化→回傳
    `GuildCommandResult`（含 `OnlineRecipientIds`）」模式。
  - `GuildService` 新增 `SyncMemberLevelJobAsync(Player, ct)` 包裝方法：角色不在公會
    （`GuildId<=0`）時提前跳過（不打登記表查找），避免非公會玩家每次升級都白跑一趟。
- **`Maple.Adapters.V113`**（`V113GuildOperationHandler.cs`）：
  - 新增 `SyncMemberLevelJobAsync(Player, sendSelf, ct)`：呼叫 `GuildService`，成功就用既有
    `BroadcastAsync` 廣播 `GuildMemberLevelJobUpdate` 給全公會（含自己，忠實對照 Java 的
    `broadcast(packet)` 不排除來源角色）。
  - 新增 `BroadcastAllianceMemberLevelJobAsync`：完全比照既有 `BroadcastAllianceMemberOnlineAsync`
    的手法（同一個檔案裡 P017 已經驗證過的模式）——用 `guildId` 排除來源公會，通知同盟裡
    其他公會的線上成員 `UpdateAllianceMember`。
  - **`V113ChannelConnectionHandler.cs`**：`SendMobKillRewardsAsync` 在既有的
    `EncodeUpdateStats` 之後，檢查 `mutation.Updates` 是否含 `PlayerStatKind.Level`（代表這次
    經驗值增加造成升級），是的話呼叫 `_guildOperationHandler.SyncMemberLevelJobAsync`。

## 🧪 測試

- `tests/Maple.Application.Tests/Guilds/GuildServiceTests.cs`：新增 2 組——
  `SyncMemberLevelJobAsync_MemberInGuild_UpdatesCachedFieldsGrantsGpAndReturnsRecipients`
  （驗證等級快取更新、GP 公式、recipients 名單；GP 斷言用「呼叫前後差額」而非固定起始值，
  因為入會本身已經依 Java 慣例加過 GP，寫死起始值會撞上這個既有行為）、
  `SyncMemberLevelJobAsync_NotInGuild_ReturnsNotInGuild`。
- `tests/Maple.Adapters.V113.Tests/ChannelGuildPacketTests.cs`：新增 2 組——
  `SyncMemberLevelJobAsync_BroadcastsGuildMemberLevelJobUpdateToSelfAndGuildmates`（驗證公會內
  廣播含自己）、`SyncMemberLevelJobAsync_MemberOfAlliedGuild_BroadcastsUpdateAllianceMemberToOtherGuild`
  （沿用既有 `NewAlliedGuildsAsync` 測試夾具，驗證同盟轉發封包欄位）。
- `dotnet build` 0 warning/0 error；全 8 個測試專案 1021 passed / 1 skipped（P058 收案基準
  1017 +4：Application 265→267、Adapters.V113 530→532）；Core/Application 禁區 grep clean。

## ✅ 結果與結論

- 零呼叫者封包掃描系列再收一件：`GuildMemberLevelJobUpdate`/`UpdateAllianceMember` 從
  「存在但沒人呼叫」變成有真實資料流入。GP（公會經驗值）系統原本只有入會/退會/踢除三個
  觸發點，這次補上「成員升級」這個 Java 原版就有的第四個來源，讓公會的 GP 累積速度更貼近
  原版體驗。
- 明確記下本次刻意不覆蓋的範圍，留給後續 P-phase 判斷是否值得投入：
  1. 道具/藥水直接領取經驗值造成的升級（`V113BuffItemHandler`）未接線——這條路徑升級頻率
     遠低於打怪，且需要在另一個檔案重複同一段「檢查 Level 更新→呼叫 SyncMemberLevelJobAsync」
     的邏輯，範圍雖小但屬於「同一個功能、不同呼叫點」的擴充，刻意留到下一個 P-phase 才做，
     避免這次改動同時touching兩個adapter檔案讓 diff 難以審查。
  2. Java 的 `sendLevelup`/`sendJobup`（`LEVEL_UPDATE`/`JOB_UPDATE` 聊天視覺通知，公會/家族
     共用同一組 opcode）完全沒有移植——純粹是聊天室裡「XX 等級提升到 N 級」的裝飾性通知，
     不影響 GP/快取欄位/UI 顯示等結構性行為，決定不在本次一併加，需要時再開新 P-phase。
  3. 職業變動（`JobId` 同步）目前恆為 no-op，因為 MapleForge 尚未實作轉職功能本身；等轉職
     落地後這裡的欄位會自然開始生效，不需要現在預先改動。
  4. 掃描出的其餘零呼叫者候選（`SpawnDoor`/`RemoveDoor`/`SpawnPortal`/`RemoveTownPortal`——
     需要 `SPECIAL_MOVE` 的傳送門建立子系統；`RemovePet` 的 hunger 分支——需要目前完全不存在
     的週期性計時器子系統；`MerchantBuyError`——待查證觸發時機）確認範圍明顯偏大，不在這次
     一併處理，個別留給使用者評估是否要投入。

## 🔗 產出

- 修改：`src/Maple.Core/Guilds/Guild.cs`、`src/Maple.Application/Guilds/GuildService.cs`、
  `src/Maple.Adapters.V113/Channel/V113GuildOperationHandler.cs`、
  `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- 修改（測試）：`tests/Maple.Application.Tests/Guilds/GuildServiceTests.cs`、
  `tests/Maple.Adapters.V113.Tests/ChannelGuildPacketTests.cs`
- 修改（測試夾具，介面新增成員）：`tests/Maple.Application.Tests/Alliances/AllianceServiceTests.cs`、
  `tests/Maple.Adapters.V113.Tests/AllianceHandlerTests.cs`
- commit：待填
