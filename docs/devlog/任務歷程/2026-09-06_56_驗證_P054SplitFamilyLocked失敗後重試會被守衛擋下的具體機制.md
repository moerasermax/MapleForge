---
編號: 2026-09-06_56
標題: P054 — 查證 SplitFamilyLocked 失敗後重試被守衛擋下的具體機制（P044 深化，確認需要「先算後mutate」重構）
類型: 驗證
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P054
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標

P053 把持久層搬進臨界區後，`DeleteJuniorAsync`/`DeleteSeniorAsync`/`SplitFamilyAsync` 共用的
`SplitFamilyLocked` 仍然是「先 mutate、後持久化，失敗不回滾」。這次不滿足於「範圍大、留給
後續」這種抽象評估，具體推演一次「持久化失敗後立刻重試」會發生什麼，確認要修好需要哪種
設計，而不是含糊地維持 P044 的判斷。

## 📋 查證過程

- 以 `DeleteJuniorAsync` 為例逐步推演：呼叫端先檢查 `!member.HasJunior(juniorId)` 才放行，
  接著呼叫 `member.RemoveJunior(juniorId)`（物件欄位異動，立即生效）、`SplitFamilyLocked`
  （可能搬遷子樹），最後 `SaveSplitAsync` 才做持久化。
- 若 `SaveSplitAsync` 丟例外（DB 失敗）：`member.RemoveJunior(juniorId)` 已經生效，
  `junior.SetSenior(0)` 也已經生效，`SplitFamilyLocked` 內部的 `_familyByCharacter`／
  `family.Members` 異動也都已經生效——這些全部**不會**因為例外而自動復原（沒有
  try/catch 回滾）。
- 玩家（或呼叫端）幾乎必然會立即重試同一個操作。重試時，呼叫端最前面的守衛檢查
  `!member.HasJunior(juniorId)` 現在會是 **true**（因為 junior 已經在上次失敗的嘗試中被
  移除了），直接回傳 `FamilyCommandStatus.InvalidOperation`——**跟 P039 在 GuildService
  `AddMemberAsync` 發現的「半成功中間態擋死重試」是同一種機制**，這裡首次針對
  `SplitFamilyLocked` 具體推演出等價的擋死路徑，而非只是抽象猜測「風險比較大」。
- 確認這不是「加個 try/catch 回滾」就能解的：`SplitFamilyLocked` 搬遷的子樹大小不定
  （`FamilyMember.GetAllJuniors` 遞迴收集），要回滾必須先在**mutate 之前**snapshot
  「哪些角色會被搬遷、他們原本的 `SeniorId`/`Junior1`/`Junior2`/所屬 `_familyByCharacter`」，
  這個 snapshot 資料結構目前完全不存在。
- **可行的修法方向（跟 Guild/Alliance 的『持久層先行』不同）**：`SplitFamilyLocked` 需要
  拆成「純函式計算階段」（只讀，算出誰要搬到哪個新/舊家族，回傳一份計畫，不動任何欄位）
  和「套用階段」（依照計畫真的異動欄位）。呼叫端流程改成：算計畫 → 用計畫的結果組出要存的
  `Family` 物件 →（此時尚未 mutate 任何東西）→ 持久化 → 持久化成功後才真的套用計畫
  異動記憶體。這樣萬一持久化失敗，記憶體完全沒被動過，重試自然安全——這才是跟 Guild/
  Alliance「持久層先行」精神一致的解法，而不是「mutate 完再回滾」。

## ✅ 結果與結論

- 沒有程式碼異動。這次把 P044「範圍大，需要先設計」的抽象判斷，具體化成一個可驗證的
  失敗案例（`DeleteJuniorAsync` 重試被 `HasJunior` 守衛擋下）與一個明確的修法方向
  （`SplitFamilyLocked` 拆成「計算計畫」+「套用計畫」兩階段，而非「mutate 後回滾」）。
- 這個修法方向本身是一次不小的重構（`SplitFamilyLocked` 目前的遞迴子樹搬遷邏輯要重寫成
  純函式計算版本），加上 `AcceptInviteAsync` 的家族合併分支也是同樣的「先算後動」候選，
  範圍仍然大到不適合在本次順手做——維持留給使用者拍板是否要投入這個重構的判斷，但這次
  提供的是具體、可驗證的問題重現路徑，不是抽象評估。

## 🔗 產出

- 無程式碼異動（純查證，具體化 P044 的抽象判斷）。
- commit：待填
