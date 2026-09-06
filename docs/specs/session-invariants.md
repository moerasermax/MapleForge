# MapleForge 新 session 常駐鐵律

> 狀態：穩定規範。這份文件定義每次新 session 必須先載入的北極星、不可變流程與證據規則。要修改本文件，必須在任務歷程與進度日誌留下原因。

## 1. 專案北極星

MapleForge 的目標不是單純讓 v113 私服能玩，而是把父親遺留下來的 OdinMS/TMS113 系伺服器，重構成自己的乾淨、可維護、可驗證、可演進框架。

核心方向：

- **v113-first，抽象 later**：先把 v113 以真實功能跑通；只有當兩個以上封包族或版本語義真的需要共用時，才提取抽象。
- **Java 是行為神諭，不是架構模板**：舊 Java server 提供 handler、流程、封包與遊戲語義的權威參考；不要搬回 OdinMS 的 static global、耦合與歷史包袱。
- **真客戶端是最終協定驗證者**：Java 不足、文件不清、或 byte layout 有疑問時，以真 v113 client、解密 capture、headless/live smoke 關閉不確定性。
- **乾淨分層比快速堆功能重要**：功能移植必須進入 MapleForge 的層次，而不是把舊 server 的形狀搬回來。
- **多實例是設計支柱**：任何 process-wide static、全域 mutable 狀態、隱性 singleton 都要先問是否會破壞多實例。

## 2. 每次新 session 載入順序

新 session 進入 MapleForge 時，先讀：

1. `AGENTS.md`
2. `docs/specs/session-invariants.md`
3. `docs/devlog/任務歷程/README.md`
4. `docs/specs/conventions.md`
5. `docs/design/重構架構設計書.md`
6. `docs/devlog/任務追蹤.md`
7. `docs/devlog/進度日誌.md`

若任務涉及協定、真客戶端、封包、WZ、工具或逆向，再加讀：

- `docs/specs/v113-protocol-spec.md`
- `docs/specs/test-strategy.md`
- `docs/design/封包擷取模式-設計.md`
- `docs/design/MapleForge方法論融合綱領.md`

## 3. 任務歷程鐵律

任何實質任務開始前，先在 `docs/devlog/任務歷程/` 建立或更新任務檔。

必要條件：

- `🎯 目標` 必須寫清楚完成判準。
- 狀態必須切到 `🚧 執行中` 才能動手。
- `⏯️ 接手點` 必須保持最新，標準是下一秒斷線仍能接續。
- 目標不能偷改；若範圍變更，必須在執行歷程寫明原因。
- 收尾時回填結果、產出、commit，並同步進度日誌；若影響里程碑，再同步任務追蹤。

## 4. 架構鐵律

層次邊界：

- `Maple.Core`：純領域模型、value objects、介面；不得知道 v113、opcode、packet byte layout、socket、LiteDB、WZ reader。
- `Maple.Application`：use case、service、handler orchestration；不得依賴 v113 adapter 或具體網路封包格式。
- `Maple.Adapters.V113`：v113 opcode、packet parser/serializer、crypto、登入/頻道封包、client-specific quirks。
- `Maple.Net`：TCP session、framing、send/receive 管線。
- `Maple.Persistence`、`Maple.Content`、`Maple.Scripting`：基礎設施實作，對 Core/Application 只暴露介面。

不可違反：

- Core/Application 不得 import `Maple.Adapters.V113`。
- Core/Application 不得出現 v113 opcode 常數或 byte layout 細節。
- Core/Application 禁止 static mutable state；MF0001 是最低門檻，不是全部保證。
- 不為了快速移植而複製 OdinMS 的全域 registry、static singleton、跨層直接存取。

## 5. 證據鐵律

MapleForge 不靠猜測堆功能。每個不確定點要標清楚證據等級。

證據優先序：

1. 舊 Java server source：handler、packet creator、資料流程、遊戲語義。
2. Java oracle/golden vector：能機器比對時優先機器化。
3. MapleForge 單元/契約/整合測試：證明新架構下語義仍成立。
4. Headless/synthetic client：證明封包時序與狀態機可以自動重演。
5. 真 v113 client capture/live smoke：證明客戶端實際接受。
6. Client instrumentation/probe：只在前面證據不足、且問題只能由 client 內部語義解開時使用。

server-to-client 特別規則：

- 沒有 Java oracle、舊 Java server capture、或真客戶端確認時，不得把 S2C fixture 升級成 golden truth。
- 未驗證 fixture 必須標記 `unverified`、`candidate` 或等價狀態。
- capture 是證據，不是設計真相；設計真相仍要回填到 protocol spec、domain model 或 persistence model。

## 6. 文件同步鐵律

改程式時同時判斷是否需要更新活文件。

必須同步的情境：

- 改 opcode、packet layout、加解密、封包時序：更新 `docs/specs/v113-protocol-spec.md` 或相關 protocol doc。
- 改 Core world/runtime semantics：更新設計文件或新增專門規格。
- 改 LiteDB/document shape、repository、持久化語義：更新 persistence 規格。
- 新增或改工具、capture、decoder、oracle：更新工具索引或相關設計。
- 修改流程規則、agent 分工、checkpoint、gate：更新 workflow 或 session invariants。
- 完成任務：更新任務歷程與進度日誌；若對里程碑有影響，再更新任務追蹤。

## 7. 取經融合鐵律

從跑跑卡丁車與黃易群俠傳搬回 MapleForge 的是方法，不是外形。

可搬：

- 跑跑的真客戶端短迴路、byte-exact replay、L1/L2/L3/L4 證據鏈。
- 跑跑的「看 client 如何消費資料，而不是只看封包名稱」。
- 黃易的 Doc-Sync Gate 思維、修復經驗庫、工具資產化、資料格式回填到 DB/spec 的紀律。
- 兩者共同的「證據留痕、錯路也要記、工具不是臨時腳本」。

不可搬：

- source-less 逆向優先級。MapleForge 有 Java 神諭，先 Java-first。
- Python/8-context 目錄形狀。MapleForge 已有 .NET clean architecture。
- 過早重 gate 導致開發者繞過流程。
- 把 capture fixture 當成設計真相。

## 8. 驗收基本線

一個功能或修補要稱為完成，至少回答：

- Java 對應來源在哪裡？若沒有 Java 來源，替代 ground truth 是什麼？
- Core/Application 的語義放在哪裡？
- v113 byte layout 是否只在 Adapter？
- 是否有針對性測試或 headless/live 驗證？
- 不確定 fixture 是否被標示為未驗證？
- 相關活文件是否同步？
- 任務歷程接手點、結果、產出是否已更新？

## 9. 安全與資產邊界

未經使用者明確要求，不修改：

- `TestMapleStoryV113_Server`
- `v113_Client`
- `_wz_ref`
- `_hare_ref`
- 姊妹專案資料夾
- 客戶端二進位、WZ 原始資產、舊 Java 權威參考檔

這些資產可以讀、比對、擷取證據；修改或覆寫需要明確批准。

## 10. Checkpoint 規則

本機曾多次藍頻或 session 中斷，因此長工作必須保留可恢復狀態。

- 使用者要求備份時，先 commit/push 再做後續改動。
- 長任務中每完成一個已驗證單元，更新任務歷程接手點。
- 需要 push 時只推 private remote 的正常分支；禁止 force push。
- 不把未驗證的大量臨時產物、live log、pid 檔當成成果提交。

## 11. P-phase 工作模式（增量清理/修補的標準流程）

> 源自 2026-09-06 P004~P010 連續執行的實測結果：每次修一個東西都發現它暴露了另一個更深的問題
> （編碼 bug → 隊伍同步缺失 → 廣播範圍 bug → 同盟聊天遺漏 → 資料寫回根因），證明「小範圍、逐一
> 驗證、發現新問題就開下一個 P」比「一次規劃全部」更穩定、更容易在 session 中斷時接續。

當任務是「P003/P004 收尾殘留 TODO」「找 bug 順手修」這類**沒有預先規劃、邊做邊發現**的增量清理
工作時，每個獨立修補單元都走以下九步，並用 `P00N` 編號累加（不重用、不跳號，接續現有最大編號）：

1. **選範圍**：挑一個範圍明確的殘留待辦（`任務追蹤.md` 開放清單）或前一個 P 發現的衍生問題；
   範圍必須小到能在一次 session 內做完驗證，不要合併多個不相關修補。
2. **對照神諭**：以舊 Java server 原始碼核對實際行為（呼叫鏈要往下追到底層實作，不能只看方法
   名稱猜語意——命名和行為不一致是舊碼常態）。
3. **放對層次**：實作進正確的 `Maple.Core`/`Maple.Application`/`Maple.Adapters.V113` 分層，
   遵守 §4 架構鐵律。
4. **補測試**：新增或更新單元測試覆蓋這次修的行為；既有測試若因為修正而需要改斷言，要在同一輪
   一併修正並確認新斷言是對的（不是為了讓測試過而遷就）。
5. **build 綠燈**：`dotnet build` 必須 0 warning／0 error。
6. **全測試回歸**：跑全 8 個測試專案（Core/Application/Adapters.V113/Persistence/Net/Content/
   Tools.PacketDecoder/Tools.HeadlessClient），確認無退化；順手 grep Core/Application 禁區。
7. **同步三本帳**：任務歷程新檔（`docs/devlog/任務歷程/`）＋`進度日誌.md` 新條目＋
   `任務追蹤.md` 新增 `P00N` 段落（比照 P002/P003 格式：D-list + 戰績摘要）。
8. **commit 歸檔**：commit 訊息結尾加「（鐵律歸檔）」，內文說明對照的 Java 來源、根因、修法、
   測試結果；push 依 §10 checkpoint 規則。
9. **發現更深問題就開下一個 P，不擴大這一個的範圍**：修補過程中若發現另一個相關但獨立的問題
   （例如「這個 bug 其實是因為另一個更底層的欄位從未被寫回」），記下來但不在同一個 P 裡順便改，
   留給下一個 P00N+1 專門處理——這樣每個 P 的 diff 都保持可獨立審查、可獨立回退。
