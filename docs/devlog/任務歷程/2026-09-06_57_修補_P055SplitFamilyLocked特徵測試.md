---
編號: 2026-09-06_57
標題: P055 — SplitFamilyLocked 特徵測試（characterization test），為 P054 提出的重構鋪安全網
類型: 修補
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P055
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

P054 確認 `SplitFamilyLocked` 需要重寫成「純函式計算計畫」+「持久化成功後才套用」兩階段
才能真正解掉重試被擋死的問題，但這個重構本身有風險（子樹搬遷邏輯複雜、目前只有 Adapters
層間接測試）。這次不直接動手重構，而是先在 Application 層替 `DeleteJuniorAsync` 補上兩個
分支的特徵測試（沒有子孫的簡單離隊 vs 帶著子孫變成新家族），確定現有行為被鎖定，未來真的
要重構時才有安全網可以驗證「重構前後行為一致」。

## 📋 背景與查證

- `SplitFamilyLocked`/`DeleteJuniorAsync`/`DeleteSeniorAsync`/`SplitFamilyAsync` 先前完全
  沒有 Application 層單元測試，只靠 `tests/Maple.Adapters.V113.Tests/FamilyHandlerTests.cs`
  間接覆蓋（透過完整的邀請/接受/刪除封包流程）。
- 逐行核對 `SplitFamilyLocked`：`member.GetAllJuniors(family)` 回傳的子樹**包含 member 自己**
  （`FamilyMember.GetAllJuniors` 的 `AddAllJuniors` 遞迴一開始就把 `member` 本身加進 result），
  所以 `subtree.Count <= 1` 代表這個角色沒有任何子孫——這種情況角色被整個移出舊家族，**不會**
  另外建立新家族；`subtree.Count > 1` 才會建立新家族、角色變成新家族的 leader、整個子樹一起
  搬過去。
- 順手發現一個沒被明確記錄過的連鎖行為：若移除帶子孫的 junior 後，舊家族只剩 leader 一人
  （`family.Members.Count <= 1`），`FinalizeOldFamilyAfterSplitLocked` 會把舊家族**整個解散**
  ——連 leader 自己的家族狀態都被清空，不只是移除 junior 那麼單純。這次的測試把這個連鎖
  行為也一併鎖定下來。

## 🔧 實作內容

- 沒有異動任何生產程式碼——這次刻意純測試，不碰 `FamilyService.cs`。

## 🧪 測試

- `tests/Maple.Application.Tests/Families/FamilyServiceTests.cs` 新增：
  - `DeleteJuniorAsync_JuniorWithNoDescendants_RemovesJuniorOnlyAndKeepsOldFamily`：junior
    沒有子孫時單純離隊，舊家族保留其餘成員，`SaveFamily` 有正確存進 repository。
  - `DeleteJuniorAsync_JuniorWithDescendant_CreatesNewFamilyAndDissolvesOldFamily`：junior
    帶著子孫變成新家族 leader，舊家族因為只剩 leader 一人而被整個解散（`FindByIdAsync`
    在 repository 裡找不到了）。
  - 新增 `NewFamilyWithMembers` 測試 fixture helper，支援建立 leader + 多個 junior（各自可
    再帶一個 grandchild）的多代家族結構，供未來要重構 `SplitFamilyLocked` 時繼續擴充案例。
- `dotnet build` 0 warning/0 error；全 8 個測試專案 982 passed / 1 skipped（P054 收案基準
  980 +2：Application 261→263）；Core/Application 禁區 grep clean。

## ✅ 結果與結論

- 這兩組測試在第一次執行就通過，沒有像 P051 那樣撞到 fixture 陷阱——因為這次先把
  `GetAllJuniors` 包含自己這個細節讀清楚才動手設計案例，而不是先寫測試再除錯，呼應
  P016/P019/P020 那則知識條目「判斷缺陷前一律先貼實際程式碼」的紀律延伸到「設計測試前
  也要先讀懂實際遞迴/分支邏輯」。
- `SplitFamilyLocked` 真正的「計算計畫＋持久化後套用」重構仍然留給後續、需要使用者評估是否
  投入（P054 已記錄具體修法方向），這次只是把安全網先鋪好。

## 🔗 產出

- 修改（測試）：`tests/Maple.Application.Tests/Families/FamilyServiceTests.cs`
- commit：待填
