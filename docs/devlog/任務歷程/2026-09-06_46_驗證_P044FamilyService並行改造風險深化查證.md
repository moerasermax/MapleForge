---
編號: 2026-09-06_46
標題: P044 — FamilyService 並行改造兩條路線都查到具體阻斷證據，維持不動手（P038 深化）
類型: 驗證
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P044
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標

P038 記錄 `FamilyService` 有跟 Guild/Alliance 同類的「registry 先於持久層異動」風險，但因為
`lock`/`Monitor` 不支援鎖內 `await`，列出三個可能方向卻未拍板。這次不滿足於重複 P038 的結論，
逐一把兩個最可能的方向（方向 1：換成 `SemaphoreSlim`；方向 3：保留 `lock`＋失敗補償）讀進
實際程式碼查證到底，確認是否真的存在低風險做法。

## 📋 查證過程

### 方向 1（換成 SemaphoreSlim，比照 Guild/Alliance）：發現具體死結風險

- 通盤讀完 `FamilyService.cs` 全部 18 處 `lock (_sync)`。發現 `SetOnline`（第 660 行）在自己的
  `lock (_sync)` 區塊內，直接呼叫**另外兩個公開方法** `Register(player, channel)`（第 623 行）
  與 `Unregister(characterId)`（第 646 行）——這兩個方法**各自也有自己的** `lock (_sync)`。
- C# `lock`/`Monitor` 本身是可重入的（同一執行緒可重複取得），所以目前這樣寫完全沒問題。但
  `SemaphoreSlim`（Guild/Alliance 用的原語）**不可重入**——如果原樣把 `_sync` 換成
  `SemaphoreSlim` 並讓 `SetOnline`/`Register`/`Unregister` 各自呼叫 `_gate.Wait()`，
  `SetOnline` 呼叫 `Register`/`Unregister` 時會在**同一執行緒**上對同一個已鎖住的號誌再次
  `Wait()`——**直接死結**。`SetOnline` 是登入/登出流程的必經路徑，一旦觸發，等同整個伺服器
  在下一次玩家登入時掛住，爆炸半徑遠大於「家族狀態偶爾跟 DB 不同步」。
- 這不是「理論上要小心」，是讀到具體的呼叫鏈才浮現的實際阻斷條件，光看 P038 原本的結論
  （只說「需要重新檢視每個方法的臨界區」）看不出這麼具體的死結點。

### 方向 3（保留 lock，加失敗補償）：發現異動範圍比 P038 估計的更大

- 追進 `AcceptInviteAsync`、`DeleteJuniorAsync`、`DeleteSeniorAsync`、`SplitFamilyAsync`
  共用的核心私有方法 `SplitFamilyLocked`（第 806 行）：這個方法會把 `member.GetAllJuniors(family)`
  算出的**整個子孫子樹**（人數不定，可能是好幾層、好幾個角色）搬到新家族，逐一改寫
  `_familyByCharacter[moving.CharacterId]`、呼叫 `ApplyFamilyToOnlineCharacterLocked`、
  並可能連帶刪除舊家族（`FinalizeOldFamilyAfterSplitLocked`）。
- 這代表：`DeleteJuniorAsync`/`DeleteSeniorAsync`/`SplitFamilyAsync` 三個方法的正確回滾**不是**
  單一 dict entry 或單一物件欄位的復原（跟 Guild 的 `TryAddMember`/`TryRemoveMember` 完全不同
  量級），而是要復原「一整個子樹搬遷」——子樹大小在編譯期未知，回滾邏輯要能處理任意數量的
  角色，而且鎖釋放期間任何一個被搬遷角色的其他並行操作都可能觀察到中繼狀態，`AcceptInviteAsync`
  的複雜度（P038 已記錄）也不是特例，是同一個共用方法造成的結構性問題，四個方法全部中獎。

## ✅ 結果與結論

- 方向 1（換原語）**現在有具體的死結證據**，不是「風險偏好」問題，是會在正常登入流程直接
  炸掉伺服器的阻斷缺陷；不能不先解掉 `SetOnline`→`Register`/`Unregister` 的重入呼叫鏈就換原語。
- 方向 3（失敗補償）的正確實作範圍比 P038 原本估計的更大——四個方法全部共用
  `SplitFamilyLocked`，回滾對象是「不定大小的子樹搬遷」而非單一欄位，倉促寫一個「只復原
  看得到的那幾個角色」的補償邏輯，遺漏邊界情況的風險很高。
- 兩條路線都不是「重新設計每個方法」這種抽象的架構顧慮，而是各自都有具體、可指出行號的
  阻斷點，維持 P038「留給使用者決定方向」的結論不變，但這次把「為什麼」講清楚到可以直接
  拿去做決策依據的程度：
  1. 要走 SemaphoreSlim 路線，必須先把 `Register`/`Unregister` 拆成不重新取鎖的私有
     `*Locked` 版本，`SetOnline` 呼叫私有版本，公開版本才包一層取鎖／釋放——這本身就是一個
     獨立的、需要仔細驗證的前置重構。
  2. 要走失敗補償路線，`SplitFamilyLocked` 需要先改造成回傳「這次搬遷了哪些角色、每個角色
     原本的 `_familyByCharacter`／`Family.Members` 歸屬」的完整快照，讓呼叫端能在持久化失敗
     時逐一復原——這個快照結構目前完全不存在，需要新設計。
- 沒有程式碼異動；不倉促選一個方向硬做，維持這幾輪 P-phase 建立的紀律。

## 🔗 產出

- 無程式碼異動（純查證，深化 P038 的結論並提供具體決策依據）。
- commit：待填
