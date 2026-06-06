---
編號: 2026-06-06_05
標題: 技術債：WZ parser 0x40 修復 + 統一線上玩家 registry
類型: 修補
狀態: 🚧 執行中
建立: 2026-06-06 (session)
更新: 2026-06-06 (session)
關聯里程碑: 平行移植後地基補強
關聯記憶: current-state-resume
關聯commit: 待填（base master 37aadfd）
---

## 🎯 目標
使用者拍板執行順序：**①技術債(本檔) → ②真客戶端 smoke 驗 SET_FIELD(#12) → ③batch-5**。本檔做技術債兩項：
1. 修 WZ parser `string block marker 0x40` bug（解鎖真實 Quest.wz/Skill.wz catalog，#11）
2. 統一多個線上玩家 registry 成單一 `IOnlinePlayerRegistry`（#13）
**驗收**：各自 worktree build+測試綠 → 整合 master → 整解 build 綠+既有 221 測試不退。

## 📋 背景
- 15 系統已平行移植完並 push(master `37aadfd`，221 測試綠，全 headless 未真機驗)。
- WZ parser bug：quest agent 載真 Quest.wz 卡 0x40 marker→現用 MinimalCatalog。權威參考 HaRepacker 原碼在 `V113/_hare_ref`。
- registry 重複：chat/buddy/party/guild 各做線上玩家/廣播 registry。
- pid：wzparser=39432 / registry=32692，worktree `V113/_worktrees/{wzparser,registry}`，base 37aadfd。

## ⏯️ 接手點（★崩潰救命行★）
> 技術債 2 Codex 並行：wzparser(39432，修 WZ 0x40，參考 `_hare_ref` HaSharedLibrary WzBinaryReader)、registry(32692，統一 IOnlinePlayerRegistry)。worktree `V113/_worktrees/{wzparser,registry}`，base master `37aadfd`。各寫 `<wt>/.codexdone`+`PORT_REPORT.md`。**下一步**：wait 收 2 份→整合到 master(這兩個互不衝突，可直接 merge+少量中央接 DI)→整解 build 綠+221 測試不退→push→清 worktree。**之後照使用者順序：真客戶端 smoke 驗 SET_FIELD/SPAWN_PLAYER(#12，需使用者在場/機器淨空/設 `Persistence:Provider=LiteDb`)→才 batch-5**。平行/整合心法見 `2026-06-06_03`/`_04`。

## ✅ 結果與結論
> 待回填。
## 🔗 產出
> 待填。
