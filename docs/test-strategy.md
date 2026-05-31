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
| **L3 合成客戶端整合** | 測試內開 server + C# 假客戶端，跑完整握手→登入→登入失敗 | `dotnet test` | ⏳ 待 Net pipeline（M1-1/5/6/7）後建 |
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

## 驗收（M1 完成定義）
- L1+L2+L3 全綠 = M1「管線打通」全自動達標。
- L4 真客戶端連線 = 最終人工確認一次（非 CI 必要）。
