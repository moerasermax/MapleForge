---
編號: 2026-06-17_01
標題: 技術債清償：runtime registry 合一 + USE_CASH_ITEM opcode 路由
類型: 重構
狀態: ✅ 完成
建立: 2026-06-17 00:00
更新: 2026-06-17 00:00
關聯里程碑: 技術債（batch-5 後）
關聯記憶: current-state-resume
關聯commit: 5ba3c87, e27ef0f
---

## 🎯 目標（執行前先寫死，過程不偷改）

> 清償 batch-5 遺留的 2 項可行動技術債：
> 1. **兩(實三)套 runtime registry 合一**：合併 `IOnlinePlayerRegistry` + `IOnlinePlayerRuntimeRegistry` 為單一介面，減少重複登記/反登記，所有消費端更新，測試全綠。
> 2. **Owl 走 USE_CASH_ITEM 路由**：新增 `USE_CASH_ITEM`(0x49) recv opcode + handler，5230000 路由到現有 OwlService 搜尋邏輯。
>
> 完成判準：Core 65 / App 118 / Adapters 226+1skip 測試全綠（計數不低於現在），build 乾淨。

## 📋 背景與假設

- IOnlinePlayerRegistry 存 OnlinePlayer(CharacterId,Name,Channel,Character,SendPacket)；IOnlinePlayerRuntimeRegistry 存 Player。兩者都以 charId 為 key、都有 token 防競態、幾乎同時 register/deregister。合併=把 Player 拉入主 registry，消費端用 Player 取 Character。
- IMapSessionRegistry(per-map 廣播) 概念不同(按地圖分群)，不合併。IFieldInstanceRegistry(FieldInstance) 也獨立。所以「兩(實三)套」＝IOnlinePlayerRegistry + IOnlinePlayerRuntimeRegistry + 部分與 IMapSessionRegistry 重疊的資訊。本次只合前兩者。
- USE_CASH_ITEM 在 Java = 0x49，v113 recv.properties 確認。Java UseCashItem() 是巨大 switch(itemId)，本次只接 5230000(Owl)，其餘 enableActions。
- 另 2 項技術債（xmas/owl/repair 空 catalog、Fix-1 Skip 測試）屬已知限制，底層系統未到位前無法修，本批不動。

## 🪜 計畫步驟

- [ ] 1. Agent-1(worktree): registry 合一——合併介面、更新實作+消費端+DI+測試
- [ ] 2. Agent-2(worktree): USE_CASH_ITEM opcode 0x49——新增 opcode、handler、5230000→owl 路由、測試
- [ ] 3. 統籌(我): 接回兩個 worktree 結果、解衝突、跑全測試

## 📜 執行歷程（邊做邊追加，附時間）

- **00:00** 勘察 4 項債：2 可行動(registry+owl routing)、2 暫不可修(空 catalog+Skip test)。派 2 agents(worktree) 平行開工。
- **00:10** USE_CASH_ITEM agent 完成(8測試)。派 ai-cli 團隊(Gemini+GPT-5.5)review。
- **00:15** GPT-5.5 抓到 P1 bug：空搜尋結果仍消耗道具(Java 只在 hms.size()>0 才扣)。Gemini 指出 CharacterMutated 硬寫 true。兩者皆修正。
- **00:20** Registry agent 完成(17檔,淨刪100行)。整合兩路到 master，全測試 418 綠(+9)。Push master e27ef0f。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> ✅ 已完成。master HEAD = e27ef0f。兩項技術債清完 push。剩餘 2 項(空 catalog / Skip test)是已知限制不動。

## ✅ 結果與結論

> 全數達標。Registry 合一：`IOnlinePlayerRuntimeRegistry` 刪除、`OnlinePlayer` 加 `Player` 欄位+`Character` 便利屬性，17 檔改動、淨刪 100 行、零行為變更。USE_CASH_ITEM(0x49)：新 handler + 9 測試，5230000 Owl 路由到 OwlService。ai-cli 團隊 review 抓到一個真 bug（空結果仍消耗），已修。

## 🔗 產出

- `5ba3c87` refactor: 合併 IOnlinePlayerRegistry + IOnlinePlayerRuntimeRegistry
- `e27ef0f` feat: USE_CASH_ITEM(0x49) + Owl 5230000 路由
- 新檔：`V113UseCashItemHandler.cs`、`ChannelUseCashItemTests.cs`
- 刪檔：`IOnlinePlayerRuntimeRegistry.cs`、`InMemoryOnlinePlayerRuntimeRegistry.cs`
- 測試：Core 65 / App 118 / Adapters 235+1skip = 418 綠(原 409)
