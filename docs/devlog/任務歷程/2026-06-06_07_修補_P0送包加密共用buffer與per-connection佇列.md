---
編號: 2026-06-06_07
標題: 修 P0 送包 bug — SendAsync 原地加密共用廣播 buffer + 無 per-connection 送包佇列
類型: 修補
狀態: 🚧 執行中
建立: 2026-06-06
更新: 2026-06-06
關聯里程碑: PerfAudit task #16
關聯記憶: current-state-resume, proactive-checkpoints-anti-crash, default-collaborate-with-ai-team
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

修掉效能稽核三方一致標的 P0 送包 bug，達成下列**可驗收判準**：

1. **正確性**：廣播同一封包給 N 個收件人時，每個收件人都收到「正確且各自只加密一次」的 payload（不再因共用 byte[] 原地加密而讓第 2+ 收件人收到二次加密/損壞 payload）。
   - 判準：新增單元/整合測試 — 對同一 plaintext 經兩條獨立 session cipher 各送一次，解密後皆 == 原 plaintext；且原 plaintext 入參未被 mutate。
2. **隔離**：慢連線不阻塞廣播迴圈與其他收件人 — 每連線一個 outbound queue（Channel）+ 單一背景 pump 序列化送出；`SendAsync` 入列即返回。
   - 判準：背壓/disposal 策略經團隊定案；測試覆蓋「入列順序 == 送出順序」與「dispose 時佇列乾淨收尾不丟例外」。
3. **P0-2 cleanup token**：斷線 cleanup 帶 session token，重連競態下舊連線不誤刪同帳號新連線的 online/map/field 狀態。
   - 判準：測試覆蓋「舊 session deregister 不影響已用新 token 註冊的 entry」。
4. 全測試維持綠（基線 221），不退既有測試；Core 零-V113 與零-static 維持。

非目標：P1/P2（AOI、dirty-tracking、鎖內 I/O）此檔不做。

## 📋 背景與假設

- 根因 A（正確性）：`MapleSession.SendAsync`(src/Maple.Net/MapleSession.cs:64) 先 `_send.Crypt(packet)` **原地加密呼叫者的 packet**，再 copy 進 framed。廣播 (`V113ChannelConnectionHandler.BroadcastPacketToOthersAsync`:1276) 對多個 session 重用同一 `packet` → 第 2+ 收件人 cipher 對「已被前一收件人加密過的 bytes」再加密 → 損壞。
- 根因 B（隔離）：`_sendLock` 是 per-session，但廣播迴圈 `foreach await` 序列等待，慢收件人卡住整圈廣播。
- `IPacketCipher.Crypt(Span<byte>)` 收 Span → 正確性修補只需「先 copy plaintext 進 framed，再 crypt framed 的 slice」。
- 每連線每方向各一個 cipher 實例（IV 隨 Crypt 演化），cipher 狀態必須序列化推進 → outbound 必須單一寫者。

## 🪜 計畫步驟

- [ ] 1. 建檔寫死目標（本步）。
- [ ] 2. 團隊 consult outbound queue 設計取捨（有界 vs 無界 Channel／背壓滿了 drop or 斷線／dispose 時 drain）。
- [ ] 3. Part A 正確性修補（中央自己做，零設計風險）：SendAsync 改 copy-then-crypt-copy + 回歸測試。
- [ ] 4. Part B per-connection queue（按團隊定案，可派 Codex）+ 整合。
- [ ] 5. Part C cleanup session token（可派 Codex 另檔）+ 整合。
- [ ] 6. 全測試綠 → commit + push → 回填三本帳。

## 📜 執行歷程（邊做邊追加，附時間）

- 讀稽核綜整 + MapleSession.cs + 廣播呼叫點，確認兩個根因。
- 團隊 consult（gpt-5.5 + Gemini 3.1 Pro）：Part B 設計**高度一致** → 有界 Channel 容量 256／滿了主動斷線（不阻塞不丟包）／await=已入列語意變更（握手 SendRawAsync 維持直接寫）／單 consumer 取代 _sendLock／關閉序＝CAS→Writer.TryComplete→await pump drain→Dispose stream。
- **Part A 完成**：MapleSession.SendAsync 改 copy-then-crypt-copy（不再 mutate packet）。
- **Part B 完成**：MapleSession 重構為有界 outbound Channel(256)+單 pump(PumpOutboundAsync)；SendAsync 入列即返回，TryWrite 失敗→Abort 主動斷線；SetCiphers 啟動 pump；DisposeAsync 優雅 drain(5s 逾時取消)。Maple.Net + Adapters.V113 編譯綠。
- **回歸測試完成**：新 tests/Maple.Net.Tests（loopback socket pair）2 測試綠 — 廣播同封包給 2 session 兩端皆正確還原+input 不被 mutate／多封包順序+IV 同步。已加入 slnx。
- **Part C 派 Codex**（PID 34484，worktree ../MapleForge-wt-p0c branch wt/p0-2-session-token）：session token 防重連競態，進行中。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 下一步：①等 Codex(PID 34484) 完成 Part C（worktree ../MapleForge-wt-p0c）→ 審 diff → merge 進 master → 跑 Application+Adapters.V113 測試。②Part A+B 已自成 checkpoint，待 commit。Part A/B 改在主樹 src/Maple.Net/MapleSession.cs（已編譯+測試綠）。基線 HEAD=7584a40。若此刻斷線：Part A+B 程式碼+測試都在主樹未 commit，先 `git add -A && git commit` 保住，再處理 Part C。

## ✅ 結果與結論

> 待填。

## 🔗 產出

> 待填。
