---
編號: 2026-09-06_40
標題: P038 — FamilyService 有同類 registry 不一致風險，但結構性差異使快速修補不安全（記錄不動手）
類型: 驗證
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P038
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標

P036/P037 修完 `GuildService` 的「registry 字典先於持久層異動」風險後，systematic 檢查其他
registry 服務（`AllianceService`/`FamilyService`/`PartyService`/`BuddyService`）是否有同類問題。
`AllianceService` 全部呼叫點都正確（持久層先行），`PartyService`/`BuddyService` 沒有這類獨立
registry 字典風險（Party 純記憶體無持久化；Buddy 資料直接掛在 `Character` 文件上）。
`FamilyService` **確認有同類風險**，但因為底層並行原語不同（`lock`/`Monitor` 而非
`SemaphoreSlim`），無法比照 P036/P037 用「單純調換順序」安全修補，需要先做設計決策，這次記錄
查證結果，不倉促動手修改並行邏輯。

## 📋 查證過程

- **`AllianceService`**：全域搜尋 `TrackAllianceLocked`/`UntrackAllianceLocked` 的每個呼叫點，
  確認 `_repository.SaveAsync`/`DeleteAsync` 全部都在登記/移除 registry 字典（`_alliances`/
  `_allianceByGuild`）**之前**執行——這個服務從一開始就寫對了，不需要修。
- **`PartyService`**：完全沒有 repository/持久化依賴（隊伍是純執行期狀態，不落地），沒有
  「記憶體跟 DB 不同步」這個問題類別存在的前提。
- **`BuddyService`**：沒有自己擁有的 registry 字典；好友關係直接掛在 `Character.BuddyList` 上，
  `_characters.UpdateAsync` 失敗頂多讓「對方」那份好友清單暫時沒存到，不會產生類似
  `_guildByCharacter` 那種「查找字典跟實際成員關係脫鉤」的結構性問題。
- **`FamilyService`**：`AcceptInviteAsync`（家族邀請接受，也是建立/併入家族的主要入口）確認有
  同類風險——`lock (_sync) { ... _familyByCharacter[...] = ...; TrackFamilyLocked(...); ... }`
  區塊**先**異動 registry 字典，`await _repository.SaveAsync/DeleteAsync(...)` 在 `lock` 區塊
  **結束之後**才執行。若持久化失敗，registry 已經認為目標角色併入了邀請者的家族，但 DB 沒有
  這筆資料——跟 P036/P037 修的 `_guildByCharacter` 問題同一類後果（角色狀態跟 DB 永久脫鉤，直到
  process 重啟）。`DeleteJuniorAsync`/`DeleteSeniorAsync`/`SplitFamilyAsync` 也是同一種
  「`lock` 區塊內異動 → `lock` 外才持久化」結構，非單一方法的個案。

## 為什麼這次不直接修

- `GuildService`/`AllianceService` 用 `SemaphoreSlim`（`_gate`），支援在鎖的保護範圍內
  `await` 持久層呼叫，所以 P036/P037 的修法只是單純把 `await _repository.XxxAsync(...)` 搬到
  registry 字典異動**之前**、留在同一個鎖的保護範圍內——低風險的順序調換。
- `FamilyService` 用 C# `lock (_sync)`（`Monitor`），**`lock` 區塊內不能 `await`**（編譯期限制），
  這是持久化被迫寫在 `lock` 區塊外的根本原因。要讓持久層搶到「先做」的順序，必須先解決
  「怎麼在異動 registry 前就確保持久化已成功」這個結構性問題，可能的方向：
  1. 把 `_sync` 從 `lock`/`Monitor` 換成 `SemaphoreSlim`（比照 Guild/Alliance），影響
     `FamilyService` 所有使用 `lock (_sync)` 的方法，是一次系統性改動，需要重新檢視每個方法的
     臨界區與並行行為。
  2. 保留 `lock`，改用「先在鎖外算好要存的 `Family` 快照＋持久化成功，再進鎖做記憶體異動」的
     兩階段寫法——但這需要重新設計每個方法的資料流（`AcceptInviteAsync` 目前的合併/搬遷邏輯
     `Members`/`_familyByCharacter` 互相依賴，要先算完整個結果才能知道要存什麼，拆開來風險
     不小）。
  3. 保留現狀，加上失敗補償（`catch` 時重新進鎖把 registry 字典異動復原）——影響範圍局限在單一
     方法，但要注意鎖釋放期間可能已有其他操作觀察到未持久化的中間狀態，補償邏輯要覆蓋這個
     race window，設計上比 Guild 的情況複雜。
  這三個方向都需要先拍板要哪一種，不適合在單一 P-phase 裡邊做邊決定，倉促選一個可能引入新的
  並行 bug（比原本的問題更難排查）。

## ✅ 結果與結論

- `FamilyService` 存在跟 P036/P037 同類的 registry-vs-DB 不一致風險，範圍至少涵蓋
  `AcceptInviteAsync`/`DeleteJuniorAsync`/`DeleteSeniorAsync`/`SplitFamilyAsync` 四個方法，
  但因為並行原語（`lock` vs `SemaphoreSlim`）跟 Guild/Alliance 不同，**沒有低風險的單純調換
  順序解法**，需要先決定要不要把 `FamilyService` 的鎖機制換成跟 Guild/Alliance 一致，這是
  一個架構層級的決策，留給使用者拍板後再排入獨立的 P-phase。
- 這次選擇「查證完整記錄、不倉促動手」而非「硬幹一個修補湊 P-phase 數字」，是刻意的判斷——
  P036/P037 的修法之所以低風險，是因為底層並行機制天生支援「持久層放進鎖的保護範圍內」；
  `FamilyService` 不具備這個前提，同一招搬過來不是「範圍小一點」而是「風險質變」，跟這次連續
  多個 P-phase 建立的「先查證範圍才動手」紀律相符。

## 🔗 產出

- 無程式碼異動（純查證結論，記錄待決策事項）。
- commit：待填
