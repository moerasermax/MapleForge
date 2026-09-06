---
編號: 2026-09-06_60
標題: P058 — 攻擊技能種類反作弊檢查（ATTACK_TYPE_ERROR，反作弊 3 件外的第 4 個獨立子項）
類型: 移植
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P058
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

延續 P033/P056/P057「拆解 registerOffense 籠統標籤」的手法，這次的候選是
`ATTACK_TYPE_ERROR`：近戰/遠程/魔法三種攻擊封包宣稱使用的技能 id 必須屬於 Java
`SkillConstants` 硬編碼的對應分類，不符合就整包丟棄。**跟前三次不同，這是一個
「會阻擋」的檢查**（Java 端 `return`，不處理傷害也不廣播），完成前必須先確認資料
逐筆核對過，把「手抄漏字誤傷合法玩家攻擊」的風險壓到最低才動手接線。

## 📋 背景與查證

- 對照 Java `PlayerHandler.closeRangeAttack`/`rangedAttack`/`MagicDamage`：三個入口都在
  解析完攻擊封包後，立刻呼叫 `SkillCollector.getInstance().isExistSkill(type, attack.skill)`
  （type 1=近戰、2=遠程、3=魔法），失敗就 `registerOffense(ATTACK_TYPE_ERROR, ...)` +
  `SkillCollector.addSkill(type, attack.skill)` + `return`。
- 追到底層 `SkillCollector.isExistSkill`：實際邏輯是委派給
  `constants.SkillConstants.isCloseRangedAttack`/`isRangedAttack`/`isMagicAttack`——三個
  純 switch-case 的硬編碼技能 id 清單（`isSpecialMove` 是 type 4，Java 裡是空 switch，
  永遠回傳 false，MapleForge 現有三個攻擊封包沒有對應 type=4 入口，不需要移植）。
- **關鍵澄清（原本以為有自我修復機制，查證後推翻）**：`SkillCollector.addSkill` 看起來像是
  把沒被分類到的技能「補登記」進清單，但實際只是塞進一個獨立的 `LinkedList<Integer>`，
  用途是每 3 小時 `outputList()` 把清單寫進 `Logs/Data/攻擊分類/*.txt` 給開發者事後追查——
  **不會**反過來影響 `isExistSkill` 的判斷結果。也就是說同一個未被分類的技能會被永久、
  每次都擋下，不是「第一次擋、之後放行」的自我學習機制。這個誤判在查證過程中被推翻，
  代表這個檢查是「真的會一直擋」的類型，跟 P033/P056/P057 純記錄的性質不同，風險判斷
  必須更嚴謹。
- 查證 MapleForge 現有三個攻擊 handler（`V113ChannelConnectionHandler.cs`
  `HandleCloseRangeAttackAsync`/`HandleRangedAttackAsync`/`HandleMagicAttackAsync`）：封包
  解析（`V113CloseRangeAttack`/`V113RangedAttack`/`V113MagicAttack`，皆有 `SkillId` 欄位）
  都已存在，純粹缺這段種類驗證——同一類「架構就位、資料沒接線」缺口。

## 🔧 資料查證方法（風險緩解的核心）

- 三份清單（近戰 94、遠程 58、魔法 35 個技能 id）用 `awk`/`grep` 對 Java 原始碼機械抽取
  （非手抄），抽取後又用 `Read` 工具整段讀出 `constants/SkillConstants.java` 對應的
  switch-case 本體逐行人工核對，確認沒有 fallthrough 陷阱、沒有多值合併漏算。
- **接線前用 `diff` 重新比對一次**：把 C# 陣列裡的數字重新抽出、排序，跟從 Java 原始碼
  重新抽取排序的清單做 `diff`，三個分類全部 `diff` 結果為空（逐一 id 完全一致）。
- 這個 `diff` 步驟也抓到一個真實的過程錯誤：近戰清單原本以為是 91 個（沿用上一輪對話
  留下的記憶），實際用 `diff` 核對後才發現 Java 原始碼是 94 個——是先前記憶裡的計數
  誤差，不是這次移植漏轉；C# 陣列本身跟 Java 逐一比對是對的，只是我自己寫的「總數斷言」
  測試數字抄錯，被測試自己抓出來。已修正測試斷言為 94。

## 🔧 實作內容

- **`Maple.Adapters.V113`**（`V113ChannelConnectionHandler.cs`）：
  - 新增 `internal static readonly HashSet<int>` 三份清單：`CloseRangedAttackSkillIds`
    （94）、`RangedAttackSkillIds`（58）、`MagicAttackSkillIds`（35），欄位設為 `internal`
    是為了讓測試能直接核對 `.Count`，跟本檔既有 `IsFarFromPortal` 等純函式擺在一起。
  - 新增 `IsCloseRangedAttackSkill`/`IsRangedAttackSkill`/`IsMagicAttackSkill` 三個靜態
    純函式（`HashSet.Contains`）。
  - `HandleCloseRangeAttackAsync`/`HandleRangedAttackAsync`/`HandleMagicAttackAsync`：在
    封包解析完成、其他處理開始前（對照 Java 呼叫 `isExistSkill` 的位置），加入對應檢查，
    不符合就 `_log.LogWarning` + `return`（不送 `EnableActions`，忠實對照 Java——Java 這裡
    沒有送任何回應封包給用戶端，跟 P056 `ITEMVAC_SERVER` 分支會送 `enableActions()` 不同）。
  - 近戰/遠程的技能 id `0`（代表「無技能的普通攻擊」）都在各自清單內，魔法清單則沒有
    `0`——忠實對照 Java（`MagicDamage` 沒有「無技能魔法攻擊」這回事）。

## 🧪 測試

- `tests/Maple.Adapters.V113.Tests/ChannelAttackSkillCategoryTests.cs`（新檔）：
  - 3 組「總數比對」（94/58/35），任何一筆清單漏轉都會先在這裡炸掉。
  - 6 組「已知合法 id 回傳 true」+ 「跨分類/不存在 id 回傳 false」的代表值測試（含清單
    起始/結尾/中段值，以及刻意驗證「近戰/遠程接受 0，魔法不接受 0」這個跟 Java 一致的
    不對稱行為）。
- `dotnet build` 0 warning/0 error；全 8 個測試專案 1017 passed / 1 skipped（P057 收案基準
  991 +26：Adapters.V113 新增 26 組）；Core/Application 禁區 grep clean。

## ✅ 結果與結論

- 反作弊拆解系列累計 4 件：P033（傳送門距離）+ P056（拾取物距離）+ P057（怪物移動異常）+
  P058（攻擊技能種類）。前三件全程只記錄不阻擋，這件是第一個「真的會阻擋玩家操作」的
  子項——決定動手的關鍵理由是：所需資料（三份技能 id 清單）已經完整且可對照原始碼機械
  抽取查證，不像 MOB_VAC（需要新增持久化計數器）或完整 CheatTracker（需要違規次數累積
  + 自動封禁架構）那樣有真正的架構缺口；剩下的風險純粹是「資料轉錄正確性」，可以用
  `diff` 逐筆核對 + 測試總數斷言雙重把關降到可接受的程度。
- 過程中發現並修正一個值得記錄的教訓：**任何從先前對話記憶帶過來的「數字」（清單長度、
  技能 id 等）在真正動手實作前，都必須重新對照原始碼查證一次**——這次近戰清單的「91」
  就是前一輪對話留下的誤記，若沒有在最後接線前重新 `diff`，測試斷言會用錯誤的數字通過
  （因為斷言數字跟清單數字都錯，但錯得一致），完全查不出來。這個「診斷模式不能只信任
  記憶」的教訓已經記進知識庫（見 [[skill-category-anticheat-porting]]，若尚未建立則待
  下次寫入）。

## 🔗 產出

- 修改：`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- 新增（測試）：`tests/Maple.Adapters.V113.Tests/ChannelAttackSkillCategoryTests.cs`
- commit：待填
