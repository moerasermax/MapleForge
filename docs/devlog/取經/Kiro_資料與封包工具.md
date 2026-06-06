# MapleForge 封包/資料基礎建設取經報告

**日期**：2026-06-06  
**來源專案**：黃逸群俠傳私服、跑跑卡丁車  
**目標專案**：MapleForge（MapleStory v113 私服重構）

---

## 一、封包永久存檔 DB 設計

### 1.1 姊妹專案的設計精華

#### 黃逸群俠傳 `packet_archive.py` 設計

**核心概念**：「pcapng 刪了知識也在」— 把側錄的加密封包解碼後永久存入 SQLite，即使原始抓包檔被刪，協定知識（opcode、payload 結構）仍可查詢。

**Schema**：
```sql
-- captures 表：一次側錄 session 的 metadata
CREATE TABLE captures (
    label TEXT PRIMARY KEY,      -- 人類可讀標籤（如 "official4_NPC商人錢莊商城"）
    pcap TEXT,                   -- 原始 pcapng 檔路徑（可刪）
    server_ip TEXT,              -- 伺服器 IP
    port INT,                    -- 伺服器 port
    ingested_at TEXT,            -- 匯入時間
    n_packets INT                -- 封包總數
);

-- packets 表：每個解碼後的封包
CREATE TABLE packets (
    id INTEGER PRIMARY KEY,
    capture TEXT,                -- FK → captures.label
    seq INT,                     -- 該 capture 內的序號
    ts_epoch REAL,               -- 精確時間戳（秒 + 小數）
    wall TEXT,                   -- 人類可讀時間 "HH:MM:SS.mmm"
    conn TEXT,                   -- 連線識別 "客戶端IP:Port"
    direction TEXT,              -- "C→S" 或 "S→C"
    main INT,                    -- 主 opcode
    sub INT,                     -- 副 opcode
    length INT,                  -- payload 長度
    payload_hex TEXT             -- 解密後 payload 的 hex 字串
);

CREATE INDEX ix_pk ON packets(capture, main, sub);
CREATE INDEX ix_pk_conn ON packets(capture, conn);
```

**關鍵優點**：
1. **解密後存檔**：存的是 `payload_hex`（已解密），不是原始加密 bytes
2. **查詢友善**：可用 `--main 0x05 --sub 0x02` 查特定 opcode 的所有封包
3. **連線追蹤**：`conn` 欄位區分多客戶端連線
4. **時間可讀**：`wall` 欄位方便對照遊戲畫面錄影

#### 跑跑卡丁車 `pcap_extract.py` 設計

**特點**：處理 TCP 重組、frame boundary 偵測、IV 追蹤，最後輸出：
- `c2s.bin` / `s2c.bin`：重組後的 TCP 流
- `tcp_order.tsv`：每個 TCP segment 的 timestamp、seq、offset
- `packet_times.tsv`：每個封包的時間資訊

**與黃易的差異**：跑跑強調「TCP 層重組 + 時間標註」，黃易強調「協定解碼 + SQLite 存檔」。

### 1.2 MapleForge 該怎麼建

#### 優勢分析

MapleForge 已有：
- **`PacketStreamDecoder`**：離線解密 V113 封包流（位元級驗證過）
- **`WindowerNdjsonDecoder`**：處理 windower hook 側錄的 NDJSON
- **`TcpStreamReframer`**：TCP 流重新切 frame

**關鍵差異**：黃易/跑跑需「在側錄工具裡解密」，MapleForge 可「存 raw frame → 用現有 decoder 離線解密」。

#### 建議設計（C# / SQLite）

```csharp
// 在 tools/Maple.Tools.PacketArchive/ 新增：
// - PacketArchive.cs（主程式）
// - PacketArchiveDb.cs（SQLite 操作）

// Schema（與黃易相容，欄位名稱微調）：
// captures 表：一次側錄 session
// - Id INTEGER PRIMARY KEY（改用 int，方便 C#）
// - Label TEXT UNIQUE
// - SourcePath TEXT（原始 NDJSON 檔路徑）
// - ServerIp TEXT
// - ServerPort INTEGER
// - RecvIvHex TEXT（4 bytes，hex）
// - SendIvHex TEXT（4 bytes，hex）
// - IngestedAt TEXT（ISO 8601）
// - PacketCount INTEGER

// packets 表：解碼後封包
// - Id INTEGER PRIMARY KEY
// - CaptureId INTEGER（FK → captures.Id）
// - Seq INTEGER
// - TimestampUsec INTEGER（微秒，避免浮點）
// - WallTime TEXT（HH:MM:SS.fff）
// - ConnectionId TEXT（客戶端 IP:Port 或 PID:Socket）
// - Direction TEXT（"C2S" 或 "S2C"）
// - Opcode INTEGER（ushort，小端）
// - Length INTEGER
// - PayloadHex TEXT（解密後）
// - PayloadRawHex TEXT（原始加密 frame，選填，供除錯）

// Indexes：
// CREATE INDEX ix_packets_capture_opcode ON packets(CaptureId, Opcode);
// CREATE INDEX ix_packets_capture_conn ON packets(CaptureId, ConnectionId);
```

#### 最小實作（P0）

```csharp
// 使用 Microsoft.Data.Sqlite（輕量、純 C#）
public sealed class PacketArchiveDb
{
    private readonly SqliteConnection _conn;

    public PacketArchiveDb(string dbPath)
    {
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        InitSchema();
    }

    public long IngestCapture(
        string label, string sourcePath, string serverIp, int serverPort,
        byte[] recvIv, byte[] sendIv, IReadOnlyList<DecodedPacket> packets)
    {
        using var tx = _conn.BeginTransaction();
        // INSERT INTO captures ...
        // INSERT INTO packets (batch) ...
        tx.Commit();
        return captureId;
    }

    public IReadOnlyList<DecodedPacket> QueryPackets(
        long? captureId = null, ushort? opcode = null, string? direction = null,
        int limit = 100)
    {
        // SELECT ... FROM packets WHERE ... ORDER BY CaptureId, Seq LIMIT ?
    }
}
```

**CLI 介面**（對照黃易）：
```powershell
# 匯入 NDJSON
dotnet run --project tools/Maple.Tools.PacketArchive -- ingest captures/windower_xxx.ndjson --label login_test

# 查詢封包
dotnet run --project tools/Maple.Tools.PacketArchive -- query --capture login_test --opcode 0x0000

# 統計 opcode
dotnet run --project tools/Maple.Tools.PacketArchive -- stats --capture login_test
```

#### 進階功能（P1）

1. **雙向關聯**：存 `PayloadRawHex`（加密 frame），方便驗證解密正確性
2. **時間窗口查詢**：`--from 14:30:00 --to 14:31:00`
3. **匯出工具**：`export --capture login_test --format json` 供其他工具分析

---

## 二、資源瀏覽器 / DB 瀏覽器對 MapleForge 的價值

### 2.1 黃易的設計分析

#### 資源瀏覽器 `app.py` + `resource_codec.py`

**功能**：
- Web UI（localhost:8765）瀏覽遊戲資源檔（Object.dat、XItem.dat、NPC_CLIENT.DAT 等）
- 自動偵測檔案格式（record size、version/count prefix、XOR 加密）
- 結構化解析欄位（道具名稱、NPC 模型、地圖名稱等）
- 圖示索引（對照 icon 編號 → PNG 檔）

**核心技術**：
```python
class ResourceCodec:
    def detect_format(self, rel_path, size, sample) -> FileFormat:
        # 根據檔名 + 特徵判斷格式
        # "Object.dat" → 0x6A 固定 record
        # "XItem.dat" → version/count + 0x70E record
        
    def classify_transform(self, ...) -> tuple[str, str, str]:
        # entropy + printable ratio + magic detection
        # 判斷：plaintext / xor_0xA6 / compressed / unknown
```

**價值**：大幅加速「研究未知檔案格式」的過程，不用每次手寫解析器。

#### DB 瀏覽器 `dbtool/app.py`

**功能**：
- Web UI 瀏覽 MongoDB（accounts、characters、inventories 集合）
- 搜尋、編輯、分頁
- 關聯查詢（character → inventory → item catalog 名稱對照）

### 2.2 MapleStory 的 WZ 資料檔

**差異**：
- 黃易：`.dat` 二進位檔（固定 record size、version/count prefix）
- MapleStory：`.wz` 資料檔（樹狀結構、IMG 檔案、多種格式版本）

**現有工具**：
- 外部工具（WzComparer、HaRepacker）已很成熟
- MapleForge 專注在「封包 ↔ 遊戲邏輯」，而非 WZ 解析

### 2.3 建議：不建 P0，可考慮 P1

**理由**：
1. **WZ 工具已成熟**：WzComparer、HaRepacker 功能完整，重複造輪子價值低
2. **MapleForge 核心在協定**：封包解碼、伺服器邏輯才是主力
3. **資源瀏覽器研發成本高**：WZ 格式複雜，需支援多版本

**若要建，最小版本（P1）**：
- **封包 opcode 對照表瀏覽器**：Web UI 查詢 `0x007B → SetField`、`0x0009 → Ping`
- **DB 瀏覽器**（若用 MongoDB）：對照黃易 `dbtool/app.py`，查詢帳號、角色、物品

---

## 三、封包/資料基礎建設最小落地清單

### P0（必建，核心價值）

| 項目 | 說明 | 預估工時 |
|------|------|----------|
| **PacketArchive 工具** | 匯入 NDJSON → 解密 → 存 SQLite | 1-2 天 |
| - `PacketArchiveDb.cs` | SQLite schema + CRUD | 0.5 天 |
| - `Program.cs` | CLI（ingest / query / stats） | 0.5 天 |
| - 整合測試 | 匯入真實 NDJSON，驗證查詢結果 | 0.5 天 |
| **README 文件** | `tools/Maple.Tools.PacketArchive/README.md` | 0.5 天 |

### P1（加分，非必要）

| 項目 | 說明 | 預估工時 |
|------|------|----------|
| **Opcode 對照表瀏覽器** | Web UI 查詢 opcode 名稱 | 2-3 天 |
| - `Maple.Tools.OpcodeBrowser` | ASP.NET Core Minimal API | 1 天 |
| - `static/index.html` | Vue/React 前端 | 1 天 |
| **DB 瀏覽器**（若用 MongoDB） | 查詢帳號、角色、物品 | 2-3 天 |
| **進階查詢功能** | 時間窗口、opcode 統計圖表 | 1-2 天 |

---

## 四、技術決策建議

### 4.1 SQLite vs MongoDB

**建議：SQLite**（與黃易/跑跑一致）

理由：
1. **封包資料結構固定**：captures + packets 表結構明確，不需 schema-less
2. **部署簡單**：單一 `.db` 檔，不需啟動 MongoDB 服務
3. **查詢效能足夠**：單表數十萬封包，index 後查詢 <10ms
4. **跨專案經驗**：黃易/跑跑都用 SQLite，可借鑒

### 4.2 存「解密後」還是「原始加密」？

**建議：兩者都存**（P0 存解密後，P1 加原始）

```sql
-- packets 表擴充
PayloadHex TEXT,        -- 解密後（P0，必存）
PayloadRawHex TEXT,     -- 原始加密 frame（P1，選填）
```

理由：
1. **解密後是主要查詢對象**：研究協定結構用
2. **原始加密供驗證**：若 cipher 有 bug，可比對 raw bytes 除錯
3. **儲存成本可接受**：一個封包平均 100-500 bytes，hex 字串加倍，一萬封包 ≈ 1-5 MB

### 4.3 時間戳格式

**建議：`TimestampUsec INTEGER`（微秒）**

理由：
1. **避免浮點誤差**：SQLite REAL 可能有精度問題
2. **方便計算**：`WHERE TimestampUsec BETWEEN 1234567890000000 AND ...`
3. **對照 pcapng**：pcapng 的 `ts_epoch` 是秒 + 小數，乘 1e6 即得微秒

---

## 五、參考檔案清單

### 黃逸群俠傳私服
- `tools/開發工具/側錄封包/packet_archive.py` — SQLite 存檔核心設計
- `tools/開發工具/側錄封包/parse_cap_ts.py` — 時間戳解析
- `tools/開發工具/packets.db` — 實際 DB 檔
- `tools/日常工具/資源瀏覽器/app.py` — Web 資源瀏覽器
- `tools/日常工具/資源瀏覽器/resource_codec.py` — 檔案格式偵測
- `tools/日常工具/dbtool/app.py` — MongoDB 瀏覽器

### 跑跑卡丁車
- `tools/pcap_extract.py` — TCP 重組 + 時間標註
- `tools/開發工具/側錄封包/packet_archive.py` — SQLite 存檔

### MapleForge 現有工具
- `tools/Maple.Tools.PacketDecoder/PacketStreamDecoder.cs` — 離線解密
- `tools/Maple.Tools.PacketDecoder/WindowerNdjsonDecoder.cs` — NDJSON 解碼
- `tools/Maple.Tools.HeadlessClient/Program.cs` — 測試客戶端
- `tools/windower/captures/*.ndjson` — 側錄檔案

---

## 六、總結

**三大建議**：

1. **封包永久存檔 DB 是 P0**：借鑒黃易/跑跑的 SQLite 設計，用現有 `PacketStreamDecoder` 解密，存 `PayloadHex`。這是「知識留存」的基礎建設。

2. **資源瀏覽器不急**：WZ 工具已成熟，MapleForge 專注在封包協定。若要建，從「opcode 對照表瀏覽器」切入。

3. **最小落地**：先建 `PacketArchive` 工具（1-2 天），驗證價值後再擴充。黃易的經驗證明「解密後存 SQLite」這條路走得通，MapleForge 可直接複製成功模式。
