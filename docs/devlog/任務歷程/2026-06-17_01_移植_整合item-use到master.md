---
編號: 2026-06-17_01
標題: 整合 port/item-use 分支到 master（batch-5 第 8 路收尾）
類型: 移植
狀態: ✅ 完成
建立: 2026-06-17 22:00
更新: 2026-06-17 22:00
關聯里程碑: batch-5 item-use
關聯記憶: v113-pivot-port-from-java, current-state-resume
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

> 將 `port/item-use` 分支的 item-use 系統整合回 master，保留 master 上已有的 7 系統不動。
> 完成判準：①master 上有完整的 item-use handler/packets/service/catalog/tests ②既有 7 系統程式碼不受影響 ③所有相關測試通過 ④worktree 清理完畢

## 📋 背景與假設

- batch-5 Codex 平行移植共 8 路，前 7 路已在 346995d 整合完成
- `port/item-use` 是最後一路，在 mf-item-use worktree 上，2 個 commit
- 不能直接 git merge（分支刪了 81 個其他系統檔案）
- 策略：手動將 13 個新增檔案複製到 master + merge 7 個修改檔案的 item-use 差異

## 🪜 計畫步驟

- [ ] 1. 複製 13 個新增檔案到 master 對應位置
- [ ] 2. 手動 merge 修改過的 7 個檔案（V113ChannelConnectionHandler 等），只取 item-use 相關改動
- [ ] 3. 跑所有相關測試確認通過
- [ ] 4. commit 到 master
- [ ] 5. 清理 worktree (mf-item-use) 和殘留目錄 (MapleForge-wt-p0c)

## 📜 執行歷程（邊做邊追加，附時間）

- **22:00** PM 評估完成，確認整合策略，準備派工
- **22:10** 讀 worktree 的 diff，確認分支只有新增 13 個檔，dispatch 尚未加進 ChannelConnectionHandler
- **22:20** 複製 13 個新增檔案到 master
- **22:25** 修改 V113ItemUseHandler：class/interface 改 public（供 DI），methods 改 internal（含 internal 型別的簽名，InternalsVisibleTo 允許 test 存取）
- **22:30** 加 item-use opcodes 到 V113ChannelOpcodes（UseSummonBag/UseMountFood/UseCatchItem/UseReturnScroll）
- **22:35** MapleServerHost 加 IItemUseCatalog/ItemUseService/IV113ItemUseRandomSource/V113ItemUseHandler DI
- **22:40** ChannelConnectionHandler 加 field/ctor/4 個 dispatch case + HandleItemUseResultAsync
- **22:45** 測試全過：Core 65、Application 118、Adapters 226+1skip

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 任務完成。

## ✅ 結果與結論

- 13 個新增檔案全複製到 master 對應位置
- DI 接線：IItemUseCatalog/ItemUseService/IV113ItemUseRandomSource/V113ItemUseHandler
- opcode dispatch：4 個 item-use opcode + HandleItemUseResultAsync（spawn/catch/warp/persist）
- Core 65 / Application 118 / Adapters 226+1skip 全通過，既有 7 系統不受影響
- 關鍵決策：V113ItemUseHandler 改 public class + internal methods，避免 CS0050/CS0051

## 🔗 產出
