---
編號: 2026-09-06_53
標題: P051 — FamilyService.Register/Unregister 抽出 *Locked 版本，解掉 SetOnline 重入呼叫（P044 前置步驟一）
類型: 修補
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P051
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

P044 查到把 `FamilyService._sync` 從 `lock`/`Monitor` 換成 `SemaphoreSlim`（比照
Guild/Alliance）前，必須先解掉 `SetOnline` 在自己的鎖區塊內呼叫公開方法
`Register`/`Unregister`（這兩個方法自己也會取 `_sync`）的重入問題——C# `lock` 可重入現狀
沒事，但換成不可重入的 `SemaphoreSlim` 會在登入/登出這個每個玩家都會走的路徑上死結。
這次動手做這個前置步驟：抽出不重新取鎖的 `*Locked` 版本，公開 API 行為完全不變。

## 📋 背景與查證

- 對照既有 `GetFamilyForCharacterLocked`/`TrackFamilyLocked`/`ApplyFamilyToOnlineCharacterLocked`
  等既有「Locked」命名慣例（本檔案已經大量使用這個模式），抽出
  `RegisterLocked(Family)`/`RegisterLocked(Player, int)`/`UnregisterLocked(int)` 三個私有方法，
  公開的 `Register`/`Unregister` 只負責取鎖後呼叫對應的 Locked 版本；`SetOnline` 改成直接呼叫
  `RegisterLocked`/`UnregisterLocked`（原本呼叫公開的 `Register`/`Unregister` 才是重入來源）。
- 全檔案 grep 確認 `Register(`/`Unregister(`（不含 Locked 尾碼）目前只有這三個公開方法本身，
  沒有其他方法從自己的鎖區塊內呼叫這兩個公開入口——這次的重構涵蓋了所有重入來源。
- 這只是**前置步驟一**，`_sync` 本身還是 `lock`/`Monitor`，尚未換成 `SemaphoreSlim`；行為
  對外完全不變（純內部重構），全 8 個測試專案跑完數字不變即可驗證。

## 🧪 測試

- 補上 `tests/Maple.Application.Tests/Families/FamilyServiceTests.cs`（**FamilyService 此前
  完全沒有 Application 層單元測試**，只有 Adapters 層的 `FamilyHandlerTests.cs` 間接覆蓋），
  針對剛重構的 `SetOnline`/`Register`/`Unregister` 補 3 組測試：leader 上線同步欄位並通知在線
  家族成員、同狀態重複呼叫回 `None`、下線後 registry 狀態仍完整（`Unregister` 沒有壞掉共用狀態）。
- **過程中意外撞到一個真實的測試 fixture 陷阱**（用獨立除錯用小型 console 專案隔離重現，
  非猜測）：`RegisterLocked(Player, channel)` 會用 `Player.Character` 上的
  `Junior1`/`Junior2`/`SeniorId`/`CurrentRep`/`TotalRep` **覆寫** registry 裡對應的
  `FamilyMember` 欄位——這是既有設計（對照生產流程：這些欄位平常由
  `ApplyFamilyToCharacter` 保持跟 `FamilyMember` 同步；玩家上線時反向同步回 registry，
  確保跨連線/跨 session 資料一致）。第一版測試直接建構「有 junior 結構的 `Family`」搭配
  「沒有對應 Junior1 欄位的裸 `Character`」呼叫 `SetOnline`，結果 registry 的家族結構被
  同步覆寫成空的——不是 `FamilyService` 的 bug，是測試 fixture 本身不寫實（現實中不會有
  「Character 完全沒同步過家族欄位、卻已經是某個家族結構化成員」這種組合）。修正做法：
  `NewPlayer` helper 補上 `seniorId`/`junior1`/`junior2` 參數，讓測試建構的 Character 跟
  FamilyMember 結構保持一致，比照真實登入流程的資料狀態。
- `dotnet build` 0 warning/0 error；全 8 個測試專案 979 passed / 1 skipped（P050 收案基準
  976 +3：Application 257→260）；Core/Application 禁區 grep clean。

## ✅ 結果與結論

- 這次意外撞到的「Character 是 FamilyMember 的鏡射來源、上線時反向同步」設計，值得記下來
  給後續要動 FamilyService 的人：任何測試/呼叫端要組出「已經是某個家族成員」的 Player 時，
  Character 上的家族相關欄位（`FamilyId`/`SeniorId`/`Junior1`/`Junior2`/`CurrentRep`/
  `TotalRep`）都要跟 FamilyMember 結構保持一致，否則 `SetOnline`/`Register(Player, channel)`
  會用 Character 的（可能是預設值的）欄位覆寫掉 registry 裡的真實家族結構。
- 前置步驟二（把 `_sync` 換成 `SemaphoreSlim`）與步驟三（在新原語下重新套用
  Guild/Alliance 那套「持久層先行」或「失敗回滾」修法）留給後續 P-phase，維持每個 P
  只做一件事的紀律，且這兩步各自都有自己的風險需要獨立驗證。

## 🔗 產出

- 修改：`src/Maple.Application/Families/FamilyService.cs`
- 新增（測試）：`tests/Maple.Application.Tests/Families/FamilyServiceTests.cs`
- commit：待填
