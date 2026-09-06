---
編號: 2026-09-06_55
標題: P053 — FamilyService 並行改造前置步驟三：持久層呼叫搬進臨界區，消除鎖釋放期間的 race window
類型: 修補
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P053
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

P052 把 `FamilyService._sync` 換成 `SemaphoreSlim` 後，架構上已經可以讓 `await` 留在臨界區
內。這次動手把原本「鎖區塊內異動、鎖釋放後才 `await _repository.XxxAsync(...)`」的 8 個方法
（`CreateFamilyAsync`/`AcceptInviteAsync`/`DeleteJuniorAsync`/`DeleteSeniorAsync`/
`UseFamilyBuffAsync`/`SetFamilyPreceptAsync`/`HandleFamilySummonAsync`/`SplitFamilyAsync`）
改成持久層呼叫留在同一段連續持有的 `_gate` 保護範圍內，消除 P044 記錄的「鎖釋放期間、
持久化還沒完成時，其他操作可能觀察到中繼狀態」race window。

## 📋 背景與查證

- 這次**刻意只做「消除 race window」**，不做「失敗時完整回滾」——兩者是不同層級的保護：
  - Race window（本次處理）：修好後，只要持久化還沒完成，其他呼叫端因為 `_gate` 還沒釋放，
    完全看不到任何中繼狀態（讀寫都會排隊等待），不會發生「看到一半異動的資料」這種問題。
  - 失敗回滾（未處理，留給後續）：如果持久化真的失敗（例外），這次的修法只是讓例外正確地
    往外拋、`finally` 正確釋放鎖，但**記憶體裡的異動不會自動復原**——跟持久層依然不同步，
    下次同一個角色重試同個操作可能會被 `TryAddJunior`/`TryAddMember` 這類「已存在」判斷擋下
    （P039 那次意外發現的教訓，見另一則知識條目）。`AcceptInviteAsync`/`DeleteJuniorAsync`/
    `DeleteSeniorAsync`/`SplitFamilyAsync` 共用的 `SplitFamilyLocked` 因為搬遷不定大小的子樹，
    要做到完整回滾需要先設計「搬遷快照」資料結構（P044 已記錄），不在本次範圍內。
- `CreateFamilyAsync` 額外處理：原本呼叫公開同步方法 `CreateFamily(int)`（自己取放鎖）取得
  family 後，鎖外才 `SaveAsync`——這是跟 P036 修過的 `GuildService.CreateGuildAsync` 完全
  同一種「registry 先於持久層」形狀的既有缺口（先前未被排進任何 P-phase）。這次改成
  `CreateFamilyAsync` 自己取鎖、把 `AllocateFamilyIdLocked`/`TrackFamilyLocked`/`SaveAsync`
  都放進同一段臨界區內，順手修掉。`CreateFamily(int)`（同步版本）維持不動，確認全域除了
  `CreateFamilyAsync` 自己以外沒有其他呼叫點。
- `UseFamilyBuffAsync`：改動稍微複雜，因為 `result`/`familyToSave` 是透過
  `UseFamilyTeleportOrSummonLocked`/`UseFamilyTimedBuffLocked` 兩個既有 helper 計算，這次
  把這兩個變數的宣告與持久化呼叫都搬進 `try` 區塊內（不再是方法層級變數），helper 呼叫本身
  不動。

## 🧪 測試

- 新增 `CreateFamilyAsync_PersistsFamilyBeforeReturning`：驗證 `CreateFamilyAsync` 回傳的
  family 確實已經寫進 repository（對照 Guild/Alliance 既有測試precedent，用真的
  `InMemoryFamilyRepository` 驗證持久化，不只驗證回傳值）。
- 其餘 7 個方法沒有新增測試——這次是「把既有的持久化呼叫搬進臨界區」的結構性調整，邏輯與
  回傳值完全不變，用「全 8 個測試專案通過數字只增加新測試那 1 筆」驗證沒有破壞既有行為。
- `dotnet build` 0 warning/0 error；全 8 個測試專案 980 passed / 1 skipped（P052 收案基準
  979 +1：Application 260→261）；Core/Application 禁區 grep clean。

## ✅ 結果與結論

- FamilyService 並行改造三步驟（P051 拆重入 → P052 換原語 → P053 持久層搬進臨界區）至此
  完成消除 race window 的部分。剩餘缺口（失敗時的完整回滾，尤其
  `SplitFamilyLocked` 搬遷子樹的快照設計）明確記錄、不在本輪動手，維持「範圍不外溢」紀律。
- 這輪順手發現並修掉一個先前沒被任何 P-phase 排到的獨立缺口（`CreateFamilyAsync` 的
  registry-先於-持久層問題）——不是本次目標的一部分，但因為改動範圍剛好覆蓋到，且修法
  跟本次其餘 7 個方法完全同構，一併處理。

## 🔗 產出

- 修改：`src/Maple.Application/Families/FamilyService.cs`
- 修改（測試）：`tests/Maple.Application.Tests/Families/FamilyServiceTests.cs`
- commit：待填
