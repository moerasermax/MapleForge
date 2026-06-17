---
編號: 2026-06-02_01
標題: 修復並驗證 AuthSuccess 登入彈回
類型: 修補
狀態: ✅ 完成
建立: 2026-06-02 10:59
更新: 2026-06-02 12:08
關聯里程碑: M2-4 / blocker#2
關聯記憶: current-state-resume, packet-capture-design, protocol-re-without-source
關聯commit: d395965
---

## 🎯 目標（執行前先寫死，過程不偷改）

> 讓真 v113 客戶端登入成功後**不再彈回登入頁，而是前進到世界/頻道選擇畫面**。
> **完成判準**：跑 `diag11-full-login.ps1` 自動登入後，肉眼確認畫面從登入頁進到世界選擇（封包層 + 視覺層雙確認）。

## 📋 背景與假設

- 症狀：server 回 AuthSuccess(0x00)、log 顯示登入成功，但真客戶端 3 秒後彈回登入頁。
- ground truth：c2s 可解密（萬用鑰匙）；s2c 無原始 server 神諭，只能靠真客戶端「肯不肯前進」當行為神諭。
- 假設：彈回是因為某個登入後 server 該主動送、卻漏送的 s2c，使客戶端與 server 互等。

## 🪜 計畫步驟

- [x] 1. windower 錄一筆登入 capture
- [x] 2. 離線解密該筆 s2c，看 AuthSuccess 之後到底送了什麼
- [x] 3. 定位漏送的封包 → 修 handler
- [x] 4. 再錄一筆回測 capture，封包層確認修補生效
- [x] 5. **肉眼確認真客戶端有無前進到世界選擇**（✅ 已前進，見 12:06 記錄）

## 📜 執行歷程（邊做邊追加，附時間）

- **10:59** windower 錄到登入 capture `captures/windower_packets_32876_2192.ndjson`。
- **11:05** 寫 `tests/.../_LiveDecodeTmp.cs` 離線解密該筆 → s2c **只有孤零零一個 0x0000 AuthSuccess**，AuthSuccess 之後什麼都沒送。
- **11:13** 定位根因：**v113 客戶端登入成功後「不主動請求」世界列表，server 須緊接著主動連送 `ServerList` + `EndOfServerList`，漏送→互等→彈回**。修補落地於 `V113LoginConnectionHandler.cs:154-159`。
- **11:14** 再錄回測 capture `captures/windower_packets_13696_2240.ndjson`（928 bytes，明顯比前幾筆大）。
- **~11:13–11:14** 🟥 **AX210 Wi-Fi 驅動藍頻 → 機器重開機，打斷在「回測還沒肉眼驗」這一刻**。
- **11:57**（恢復後）離線解碼 11:14 回測 capture，**封包層確認修補生效**：s2c 從 1 個變 3 個 —— `0x0000 AuthSuccess` + `0x0002 ServerList(len38, world"8838")` + `0x0002 0200FF(EndOfList)`。ServerList + EndOfServerList 確實主動送出了。
- **12:00** 接續視覺驗證。清殘留殭屍 `dotnet.exe`(PID 21800) + build-server shutdown；**先 build Maple.Host.Login 確保 11:13 修補進 binary**(diag11 用 --no-build,怕跑到舊檔,0/0 綠)；確認 windower_host.exe 存在 → 起 diag11。
- **12:03（第一次跑 diag11）** 帳密注入成功(截圖見 testuser + 密碼遮罩),但 **server 沒收到登入、無 "✓ 登入成功"** → 畫面仍停登入頁。根因=**使用者同時在操作機器(看對話/切視窗)搶走前景焦點 → 登入鈕點空**。GUI 自動化對前景焦點敏感。
- **12:06（第二次跑 diag11，使用者手放開不碰機器）** **server log `[v113] ✓ 登入成功 account='testuser' (id=1)`** + 修補主動連送 ServerList+EndOfServerList。WndProc probe 仍 0xE5=0、49 個 WM_CHAR(方法A穩固)。
- **12:07 ★決定性視覺確認** `full-4-after-login.png` 放大 → 畫面**已從登入頁前進到「選擇伺服器/頻道 SELECT WORLD/CHANNEL」**,世界按鈕顯示 **「雪吉拉」(Scania,= WorldName 預設值)**。**客戶端不再彈回 → blocker#2 解決,封包層+視覺層雙驗證。** ServerList 38-byte 布局欄位正確(world 名渲染對),全程未翻 Java。
- **12:08** 清 8 個殘留 dotnet(diag11 子進程+build server)→ build-server shutdown 全清 0 殘留。回填三本帳。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> ✅ 本任務完成（blocker#2 關閉）。**下一個任務（另開歷程檔）= blocker#3「沒角色/進地圖」**：本輪 seed 已建 TestHero，點「雪吉拉」→ 應觸發 CharlistRequest 看 CharList 是否顯示 TestHero → 選角 → CHAR_SELECT→SERVER_IP→切 Channel(8585)→進地圖。下次驗證從「世界選擇畫面點雪吉拉」接續即可。
> ＊跑 diag11 等 GUI 自動化時：**使用者勿同時操作機器**（前景焦點被搶會讓點擊落空，本任務第一次失敗即此因）。

## ✅ 結果與結論

> **達標。** blocker#2 解決：漏送 ServerList 為彈回根因，修補使 server 在 AuthSuccess 後主動連送 ServerList+EndOfServerList → 真客戶端登入成功後**前進到「選擇伺服器/頻道」畫面（世界「雪吉拉」），不再彈回**。封包層（解碼 capture：AuthSuccess+ServerList+EndOfList）+ 視覺層（截圖）雙重驗證。
> **學到/可轉移心法**：
> 1. s2c 缺欄位的定位與 ServerList 38-byte 布局正確性，**全程沒翻 Java**——靠「解密 c2s/s2c 位元組 + 真客戶端有無前進」即可自證，這套對黃易（無 server）原樣可用。
> 2. **GUI 自動化的致命脆弱點＝前景焦點競爭**：使用者同時操作機器會讓 SetForegroundWindow+滑鼠點擊落空（第一次跑就因此失敗）。教訓：無人值守 GUI 驗證期間機器須淨空，或改用不依賴前景焦點的點擊法（PostMessage/座標直送）。
> 3. `0x01(Login)=0` 計數器是假訊號（handled opcode 不印 "opcode=0x.."），真訊號是 server log `✓ 登入成功`。

## 🔗 產出

- 改檔：`src/Maple.Adapters.V113/Login/V113LoginConnectionHandler.cs:154-159`
- 解碼工具（暫存，用完即刪）：`tests/Maple.Tools.PacketDecoder.Tests/_LiveDecodeTmp.cs`
- fixture：`tools/windower/captures/windower_packets_13696_2240.ndjson`（修後）、`..._32876_2192.ndjson`（修前對照）
- 視覺證據：`diag-shots/full-4-after-login.png`（世界選擇畫面）、`_zoom-worldselect.png`（「選擇伺服器/頻道」+「雪吉拉」）
- 記憶更新：`current-state-resume` blocker#2 → 關閉
- 已刪暫存：`tests/.../_LiveDecodeTmp.cs`（用完即刪）
- commit：待填（使用者要求時才 commit）
