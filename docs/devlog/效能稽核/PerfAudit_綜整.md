# 效能稽核綜整（對照 OdinMS 反模式）

> 日期：2026-06-06　base master `f219355`　稽核：3 Codex(gpt-5.5 xhigh) 唯讀
> 詳見 `PerfAudit_A_並發與共享狀態.md` / `PerfAudit_B_阻塞IO與async.md` / `PerfAudit_C_DB寫入與廣播配置.md`

## 總評（誠實）
**好消息**：.NET 重寫在關鍵處確實比 OdinMS 體質好——①async 紀律守住（**無** `.Result/.Wait/GetResult`、`async void`、`Task.Run` 包同步）②**無每動作打 DB**（移動/聊天/撿物/加經驗/穿脫都不即時寫 DB）③零-static 大致守住。所以「改寫提升效能」的架構方向是對的。
**但有真地雷**：3 方獨立稽核**一致指向同一個 P0**——送包/廣播的 buffer 共用 + 原地加密。

## 修法清單（嚴重度排序）

### 🔴 P0（正確性+效能雙殺，最先修）
1. **`MapleSession.SendAsync` 原地加密共用廣播 byte[]**（A/B/C 三方都標）。廣播重用同一封包 → 第 2 個以後收件人收到二次加密/損壞 payload。**且無 per-connection outbound queue** → 慢連線阻塞其他人、並發送包錯包。
   - 修：加密**寫進 per-send 複本**（別 mutate 共用 buffer）；每連線一個 outbound queue/Channel 序列化送出。
2. **cleanup 無 session token**：舊連線斷線時可能刪掉同帳號新連線的 online/map/field 狀態（重連競態）。
   - 修：cleanup 帶 session token，只清自己那個。

### 🟠 P1（擴展性/並發瓶頸）
3. **鎖內做 I/O / 粗鎖**：guild registry 全域 semaphore 且**鎖內跑 repository I/O**；field 用 scattered `lock(field)`（Field actor 未落地）。→ 並發一上來就卡。
   - 修：I/O 移出鎖；Field 改 actor/單寫者模型或 ConcurrentDictionary + 細粒度。
4. **整物件 DB 替換**：所有 repository 存檔 = 整份 `Character/Account/Guild` replace（cashshop/公會/倉庫close/登出觸發 full replace）。
   - 修：dirty-tracking 增量寫 / 欄位級更新 / 週期 flush 合批。
5. **無 AOI**：`GetOthers().ToList()` 廣播給全圖所有人 + per-call 配置。大地圖 O(N) 全廣播。
   - 修：AOI（視野範圍訂閱）；避免 ToList 配置。

### 🟡 P2
6. 熱路徑配置/GC：PacketWriter/Send frame 每封包配置、每封包 buff expiry 全掃描、LINQ/裝箱 → buffer pool/Span/節流。
7. LiteDB 假 async、封包擷取同步寫檔、NPC script/WZ 首次載入同步 I/O、熱路徑 logging 未 buffered。

## 建議下一步
**先修 P0-1（送包 buffer 複製 + per-connection queue）**——它同時是正確性 bug（廣播損壞）和效能瓶頸，CP 值最高，且與真客戶端 smoke(#12) 相關（廣播損壞可能讓多人同圖出問題）。P0-2 一起修。P1 視目標再排。
（可再開平行 worktree 修，或單支修+真機驗。）
