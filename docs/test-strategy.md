# 全自動測試策略

> 目標：**不用手動開客戶端**，靠 `dotnet test` 就能驗證 server 對不對（含 bit 級協定正確性）。

## 核心難題與洞察
要自動驗證 v113 server，需要一個會講協定的「對手」，且要能判斷 cipher 是否**真的**與客戶端相容。
**關鍵洞察**：舊 Java 伺服器當年能與真客戶端通訊 → 舊 Java cipher ≡ 真客戶端。
所以「C# 逐 byte 等於舊 Java」**≡**「等於真客戶端」。→ 用舊 Java 當預言機即可全自動關閉 bit 級盲點。

## 四層測試金字塔

| 層 | 內容 | 自動化 | 狀態 |
|---|---|---|---|
| **L1 單元** | cipher round-trip / 結構自洽（15 項） | `dotnet test` | ✅ |
| **L2 黃金真值** | 舊 Java 預言機產 byte 級向量，C# 逐 byte 比對（5 項） | `dotnet test`（向量已烤入） | ✅ |
| **L3 合成客戶端整合** | 真 loopback socket，C# 假客戶端跑完整握手→登入→登入失敗 | `dotnet test` | ✅ `LoginPipelineIntegrationTests` |
| **L4 真封包**（選配） | 真客戶端抓一次封包存 fixture，replay 比對 | 半自動一次 | ⏳ 選配，最終確認 |

## L2 預言機操作（如何重生黃金向量）
1. 來源：`tools/oracle/GoldenVectors.java`，用舊專案 `build/classes` 的 `tools.MapleAESOFB`。
2. 產生：
   ```
   javac -cp <old>/build/classes -d tools/oracle tools/oracle/GoldenVectors.java
   java  -cp "<old>/build/classes;tools/oracle" GoldenVectors
   ```
3. 把輸出 hex 烤進 `tests/.../GoldenVectorTests.cs`。
4. 需要新向量（新封包類型）時，擴充 harness 重跑即可。

## L3 設計（待實作）
- 測試內以 Generic Host 在 ephemeral port 啟動 server 實例。
- C# `TestClient`：連線 → 收 getHello（未加密）→ 解析 version/recvIv/sendIv → 建鏡像 cipher（client.recv = server.send 參數）→ 送 `LOGIN_PASSWORD` → 收 `LOGIN_STATUS` 解密 → 斷言 reason。
- L2 已保證 cipher 正確 → L3 用我方 cipher 可信，專注驗證**接線**（framing/session/握手/opcode 路由/回應）。

## L4 真客戶端自動化（偵察後設計）

**偵察結論（v113_Client/）**：
- `登入器.bat` = `start MapleStory.exe 127.0.0.1 8484` → **客戶端直接吃 `<ip> <port>` 參數，不用 patch**。
- `SolusTech.ini`：`Windowed=1`（原生視窗化）、`V5=0`（V5 反作弊關）。
- `HShield/` 空資料夾 → HShield 未啟用。
- dxwnd.ini 路徑失效 → 不依賴 dxwnd，靠 SolusTech 原生視窗化。

**核心理念**：判定在「我們自己的 server 端」，不讀客戶端畫面。客戶端＝流量產生器，server＝裁判。

**L4a 握手 smoke（完全自動、零 GUI）**：
```
啟動 MapleStory.exe 127.0.0.1 <port>
  → server log：TCP 連線 + 送 getHello + 客戶端未斷線（接受我方加密握手）
  → N 秒內「連線+握手+未斷」= PASS → 殺 MapleStory.exe + 清 .dmp
```
驗證「真客戶端接受我方握手」，不需操作 GUI（客戶端啟動即自動連線握手）。

**L4b 登入 E2E（最終確認、定期跑）**：
- 讓客戶端送出加密 `LOGIN_PASSWORD`：優先用 `單人測試登入器.exe`（若會自動登入）；否則影像式 GUI 自動化（AutoHotkey/截圖比對，因 DX8 自繪無標準控制項）。
- 判定：**server 成功解密到 `LOGIN_PASSWORD`** = cipher+握手+framing 全對。
- 因 L2/L3 已鎖正確性，L4b 為定期確認，非每 commit（降低 GUI 自動化脆弱性）。

**風險對策**：GUI 脆弱→優先 server 端觀察＋自動登入器；崩潰殘留→腳本殺乾淨+清 dmp；需 server 先支援握手→等 M1-5。

**實作物（待 M1-5 後）**：`scripts/run-client-smoke.ps1`（起 server→launch client→輪詢 server log 的握手信號→timeout→殺 client→回傳 exit code）。server 端需在各階段印明確 log 行（連線/送握手/解密封包/收到登入）供腳本判讀。

## 驗收（M1 完成定義）
- L1+L2+L3 全綠 = M1「管線打通」全自動達標。
- L4a 真客戶端握手 smoke 自動通過；L4b 真客戶端登入 = 最終人工/定期確認。
