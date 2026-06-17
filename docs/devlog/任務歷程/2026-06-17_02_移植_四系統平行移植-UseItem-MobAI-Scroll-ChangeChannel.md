---
編號: 2026-06-17_02
標題: 四系統平行移植：USE_ITEM/怪物AI/強化卷軸/換頻道
類型: 移植
狀態: ✅ 完成
建立: 2026-06-17 01:00
更新: 2026-06-17 01:00
關聯里程碑: 「正常打怪練等」關鍵鏈
關聯記憶: current-state-resume, v113-pivot-port-from-java
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

> 移植四個讓玩家能「正常打怪練等」的核心系統：
> 1. **USE_ITEM 消耗品**（藥水/食物）— 補血補魔
> 2. **怪物 AI/移動**（MOVE_LIFE/AUTO_AGGRO）— 怪物會動、會追人
> 3. **強化卷軸**（USE_UPGRADE_SCROLL）— 裝備成長
> 4. **換頻道**（CHANGE_CHANNEL）— 多人基礎
>
> 完成判準：各系統 opcode dispatch 接通、handler+service 落地、測試全綠（不低於 418）、build 乾淨。

## 📋 背景與假設

- 四系統幾乎零重疊（不同 opcode/handler/service），可 4 路 worktree 平行。
- 共改點：V113ChannelOpcodes.cs（各加不同 opcode）、V113ChannelConnectionHandler.cs（各加不同 case）、MapleServerHost.cs（DI）→ 統籌中央合併。
- Java 參考：InventoryHandler.UseItem ~263L / MobHandler 452L / InventoryHandler.UseUpgradeScroll ~200L / InterServerHandler 353L。
- 先派偵察兵讀 Java 取精確 briefing 再派工人。

## 🪜 計畫步驟

- [ ] 1. 偵察兵讀 Java 四塊取封包格式+邏輯 briefing
- [ ] 2. 派 4 agent(worktree) 平行移植
- [ ] 3. ai-cli 團隊 review 各路產出
- [ ] 4. 統籌中央合併到 master + 全測試

## 📜 執行歷程（邊做邊追加，附時間）

- **01:00** 派偵察兵讀 Java 四塊。
- **01:10** 偵察兵回報。確認：Mob 有 MoveTo()、Equip 有全屬性欄位、WZ 缺 item spec → 用 HardcodedCatalog。換頻道=單 process MVP（save→reconnect same port）。
- **01:15** 先派 4 路內建 agent，使用者要求改 ai-cli 團隊。
- **01:25** 停掉 4 路內建 agent。Mob AI 恰好做完（250 測試綠），cherry-pick 到 master(6073697)。
- **01:30** 寫 prompt 檔(docs/devlog/ai-cli-prompts/)，派 3 個 Codex-ultra：USE_ITEM(PID 23956) / Scroll(PID 7668) / ChangeChannel(PID 26356)。
- **鐵律更新**：使用者要求「派 ai-cli 不用內建 Agent」升級為鐵律（見記憶 use-ai-cli-not-builtin-agents）。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> ✅ Mob AI 已整合 master(6073697)。3 個 Codex-ultra 進行中：USE_ITEM(PID 23956)/Scroll(7668)/ChangeChannel(26356)。master HEAD = 6073697，433 綠(Core 65/App 118/Adapters 250+1skip)。等 Codex 完成後整合 + Gemini/GPT review。

## ✅ 結果與結論

> 全數達標。4 系統移植完成：Mob AI(內建agent恰好跑完) + USE_ITEM/Scroll/ChangeChannel(3x Codex-ultra ai-cli)。
> 測試 Core 65 / App 134 / Adapters 263+1skip = **462 綠**（原 418 → +44）。

## 🔗 產出

- `6073697` feat: 怪物AI MOVE_LIFE(0xB6) + AUTO_AGGRO(0xB7)
- `33e59ff` feat: USE_ITEM(0x42) + 強化卷軸(0x50) + 換頻道(0x1F)
- 新檔 14 個（Core 2 + App 4 + Adapters 4 + Tests 4）
- prompt 檔：`docs/devlog/ai-cli-prompts/`（可重用模式）
