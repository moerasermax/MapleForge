---
編號: 2026-09-06_54
標題: P052 — FamilyService 並行改造前置步驟二：`_sync`（lock）換成 `_gate`（SemaphoreSlim）
類型: 修補
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P052
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

延續 P051 的前置步驟一（拆掉 `SetOnline` 對 `Register`/`Unregister` 的重入呼叫），這次做
步驟二：把 `FamilyService._sync`（`object` + `lock`/`Monitor`）換成 `SemaphoreSlim`（比照
Guild/Alliance 的 `_gate` 命名與寫法）。**只換原語本身**，18 個臨界區的邏輯與範圍完全不動
（持久層呼叫是否要搬進臨界區、失敗要不要回滾，都是後續獨立 P-phase 的事）。

## 📋 背景與查證

- P051 已確認全檔案唯一的重入來源（`SetOnline` 呼叫公開 `Register`/`Unregister`）已解掉，
  且沒有其他公開方法從自己的鎖區塊內呼叫另一個公開的鎖方法（全檔案 grep 每個公開方法名
  逐一核對呼叫點）。`CreateFamilyAsync` 呼叫 `CreateFamily` 是在鎖**外**的循序呼叫，不是
  巢狀鎖，不受影響。
- 全檔案 18 個 `lock (_sync)` 逐一轉換：11 個同步方法（`CreateFamily`/`InviteToFamily`/
  `DenyInvite`/`GetFamilyInfo`/`GetFamilyPedigree`/`GetFamilyForCharacter`/`GetFamily`/
  `Register` ×2/`Unregister`/`SetOnline`）改用 `_gate.Wait()`；7 個 async 方法
  （`AcceptInviteAsync`/`DeleteJuniorAsync`/`DeleteSeniorAsync`/`UseFamilyBuffAsync`/
  `SetFamilyPreceptAsync`/`HandleFamilySummonAsync`/`SplitFamilyAsync`）改用
  `await _gate.WaitAsync(ct).ConfigureAwait(false)`。每處都是 `try { 原本 lock 區塊內容一字
  不動 } finally { _gate.Release(); }`——早期 `return` 語句在 try/finally 下行為跟原本的
  `lock` 完全一致（都會先跑完 release 再真正返回），純屬機械式代換。
- `SemaphoreSlim` 不像 Guild/Alliance 那樣額外做 `IDisposable`（比對 `GuildService` 同樣
  沒有實作 IDisposable，process 生命週期單例，不處理視為既有慣例，非本次新決策）。

## 🧪 測試

- 沒有新增測試——這是純粹的並行原語代換，行為完全不變，用「全 8 個測試專案通過數字跟
  P051 完全一致」作為驗證（979 passed / 1 skipped，無任何一項增減）。
- `dotnet build` 0 warning/0 error；Core/Application 禁區 grep clean。

## ✅ 結果與結論

- 前置步驟二完成。此刻 `FamilyService` 的並行機制已跟 Guild/Alliance 一致
  （`SemaphoreSlim` + `await` 可留在臨界區內），P036/P037/P039/P040 那套「持久層先行」或
  「失敗回滾」修法現在**架構上**可以安全套用了——但這次刻意不順手做，因為套用修法本身
  需要對每個方法的資料流重新設計（尤其 `AcceptInviteAsync`/`DeleteJuniorAsync`/
  `DeleteSeniorAsync`/`SplitFamilyAsync` 共用的 `SplitFamilyLocked` 會搬遷不定大小的子樹，
  P044 已記錄這是獨立且更大的風險），維持每個 P-phase 只做一件事、範圍不外溢的紀律。
- 步驟三（重新套用 Guild/Alliance 那套修法，或針對 `SplitFamilyLocked` 設計快照/回滾）
  留給後續 P-phase。

## 🔗 產出

- 修改：`src/Maple.Application/Families/FamilyService.cs`
- commit：待填
