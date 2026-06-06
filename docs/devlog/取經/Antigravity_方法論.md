# 跑跑卡丁車開發取經：方法論與工作流導入報告

> **報告撰寫人**：Antigravity (MapleForge 研究員)  
> **日期**：2026-06-06  
> **目標**：萃取「跑跑卡丁車」私服重構專案的方法論精華，平移並優化 MapleForge（.NET / C#）的工作流與驗證機制。

---

## 1. 跑跑卡丁車「真機迭代迴圈」與 Java 原碼的平移

### 跑跑卡丁車的核心迭代迴圈 (The Loop)
跑跑專案透過以下迴圈實現高效的真機迭代：
1. **起「透明 server」**：啟用 Host 並開啟 Verbose log，原生解密所有 RX/TX 封包，記錄明文封包流。
2. **跑真實客戶端**：直接以 `-profile:launcher` 繞過原啟動器啟動遊戲。
3. **定位卡點**：觀察 log 停在 client 送出的哪個請求（Pq），判定 client 當前正在等待哪個回應（Pr）。
4. **精準對照原碼**：前往 `decompiled/` 逐行研讀 Stub Server 的 Handler 邏輯與 byte 級欄位。
5. **Byte 級補齊**：在 Host 忠實補上封包欄位，不猜測、不簡化、不盲目使用 `no-op`。
6. **重跑真機驗證**：觀察 client 是否順利往前推進，並在取得里程碑進展時立即 `git commit`。

### 對照 MapleForge：Java 原碼（TestMapleStoryV113_Server）的定位與平移
跑跑卡丁車的 `decompiled/`（Stub Server 原碼）等於持有「Server 行為的標準答案」。在 MapleForge 中，我們的**舊 Java 原碼（OdinMS 系）同樣扮演了這個標準答案的角色**。

#### 方法論平移策略：
* **以 Java 為「行為預言機」（Oracle）**：當 MapleStory 客戶端在某個階段（例如換地圖、NPC 互動）卡住或斷線時，不要盲目猜測協定或隨意 no-op。應精準定位 Java 原碼中對應的 Handler 封包欄位與寫入順序。
* **分層移植而非 1:1 照抄**：Java 原碼結構混亂（充斥 static 欄位與高耦合）。平移時，必須將 Java 的封包 byte 級規格鎖在 `Maple.Adapters.V113`，而將領域邏輯解耦重構至 `Core`，維護 MapleForge 的架構北極星（Core 零 V113 依賴）。

---

## 2. 最值得 MapleForge 借鑑的「坑/教訓」與「驗證手法」

### 💡 關鍵教訓與踩坑點

1. **別猜欄位，照原碼進行 Byte 級精準對照**
   * **教訓**：在跑跑專案中，猜測 `UserNO`、賽道名或直接把 Client 封包改裝成 Server 回包發送，都導致了白費工。
   * **啟示**：MapleStory 的封包結構更為龐大複雜，一個 byte 的偏移就會造成客戶端記憶體損壞（A/B crash）。我們必須在 `Adapters.V113` 建立嚴格的 Byte 級單元測試，對照 Java 的輸出。
2. **留意「一對多發送（1:N Broadcast/Push）」**
   * **教訓**：一個 C2S 請求不一定只回一個 S2C。例如 `PqGetRider` 會先 push 整批道具清單封包，最後才回 `PrGetRider`。
   * **啟示**：MapleStory 在執行交易、組隊、升級等動作時，常會同時觸發多種 Stat 變更、庫存更新與周圍玩家廣播。在移植 Java 邏輯時，必須完整追蹤該 Handler 呼叫的所有 `sendPacket` 點，避免漏包導致 client 狀態阻塞。
3. **辨識「背景 Keepalive」與「阻塞性等待」**
   * **教訓**：跑跑專案曾被持續重送的 `PqServerSideUdpBindCheck` 誤導，花了兩輪時間去調試 UDP，最後發現它只是背景 Keepalive。
   * **啟示**：MapleStory 有頻繁的 Ping/Pong 心跳包。當看見封包無限重送時，先檢查**重送的同時是否有冒出其他新業務封包**。若有，說明此包為背景背景運作，並非當前阻塞的主因。
4. **避免 Console I/O 阻塞主發送路徑**
   * **教訓**：跑跑專案因為在 `SendPayloadAsync` 前同步進行了全域 Console 日誌包裝與印出，導致發包被 Console 鎖阻塞，進而觸發 client gated timer 逾時。
   * **啟示**：MapleForge 應採用非同步日誌記錄器，或將 `TX` 日誌置於資料實際寫入 Socket 之後，避免 verbose logging 的 I/O 延遲破壞遊戲時序（timing）。

### 🛠️ 驗證手法導入

* **雙層 Replay 測試模型**：
  * **Byte 物理層**：保留目前的 `PacketDecoder` 與離線 byte 級比對測試（ReplayComparisonTests），確保靜態封包組裝與舊 Java 完全一致。
  * **Timing 狀態層**：不能只驗「若一口氣餵入所有 C2S，Server 會吐出正確的 S2C」。必須引進 **Gated Replay Test**，模擬「送出一個 C2S -> 等待 S2C 抵達 -> client 狀態機解鎖 -> 再送下一個 C2S」的時序驗證，在測試中提早捕捉時序回歸問題。

---

## 3. 「真相來源優先序」與「真機是金標準」原則的套用

### 真相來源優先序（MapleForge 版）

為避免開發時在多份參考資料中迷失，確立以下優先序：

$${\color{goldenrod}\text{Java 權威原碼（標準答案）}} \rightarrow {\color{lightgreen}\text{真客戶端解密封包 Log（事實）}} \rightarrow {\color{skyblue}\text{Winsock 側錄資料}} \rightarrow {\color{gray}\text{黑箱猜測（禁止）}}$$

1. **Java 舊源碼 (TestMapleStoryV113_Server)**：作為行為與欄位的 Oracle，是最高指導原則。
2. **真客戶端解密 Log (MapleForge Capture)**：藉由 `MAPLEFORGE_CAPTURE=1` 在真機運行中錄下的封包，提供實際運行的事實。
3. **側錄與外部工具 (Winsock/Wireshark)**：當 Java 邏輯與真實 client 行為不符，或涉及未加密握手/IV 交換時使用。

---

### 「真機是金標準」與解決 S2C 無 Ground Truth 痛點

#### 現存痛點
MapleForge 目前在 Client $\rightarrow$ Server (C2S) 有明確的解密 Log 對照；但在 Server $\rightarrow$ Client (S2C) 方向，由於 client 是閉源黑箱，我們無法直接得知 client 解碼是否成功，導致很多 S2C 測試向量只能標記為 `unverified`，避免升級為黃金測試。

#### 借鏡與解決方案：利用 Java 作為 S2C 預言機
我們可以將「舊 Java Server」視為 S2C 的 Ground Truth 產生器，並套用跑跑卡丁車的側錄理念：

```
+------------------+                   +------------------+
|  舊 Java Server  |--[S2C 明文流]---->|  MapleForge Test |
|  (Ground Truth)  |   (Winsock 擷取)  |   (黃金測試斷言)  |
+------------------+                   +------------------+
         |                                      ^
    [真客戶端登入]                             [Byte-exact 比對]
         |                                      |
         v                                      |
+------------------+                   +------------------+
|   真實客戶端     |                   |    MapleForge    |
|   MapleStory     |                   |    新重構 Server  |
+------------------+                   +------------------+
```

1. **雙向 Winsock 擷取（雙軌實證）**：
   * 啟動舊 Java Server，用掛載了 Windower Winsock Hook 的真客戶端進行登入與遊戲操作。
   * 將 S2C 封包完整錄製下來。利用解密工具將這些 S2C 封包還原為明文 Byte 串流。
2. **建立「S2C 黃金向量」庫**：
   * 將這些從舊 Java Server 側錄並解密出來的 S2C 明文，作為 `verified` 的黃金資料（Golden Fixtures）。
3. **在 MapleForge 進行 Byte-exact 斷言**：
   * 在 MapleForge 的單元測試中，模擬相同的業務場景，將 MapleForge 新架構輸出的 S2C 封包與剛才錄製的 S2C 明文進行 Byte-exact 比對。
   * **若 Byte 完全一致，該 S2C 封包即可從 `unverified` 晉升為「黃金測試」**，徹底解決 S2C 方向缺乏驗證基準的痛點。
4. **真客戶端自動化驗收**：
   * 每次重構或大幅度封包調整後，必須執行真客戶端自動化登入煙霧測試（Smoke Test）。「單元測試全綠 $\neq$ 遊戲不 Crash」，唯有真機順利進入大廳且不閃退，才算真正通過驗收。
