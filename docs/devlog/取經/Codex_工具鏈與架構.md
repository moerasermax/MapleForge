# Codex 取經報告：黃逸群俠傳工具鏈、Doc-Sync Gate、DDD 架構

> 產出日期：2026-06-06  
> 取經對象：`D:\WorkSpace\AI_Lab\研究中\黃逸群俠傳私服`  
> MapleForge：`D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\MapleForge`  
> 讀取範圍：黃逸 `DOC_SYNC_GATE.md`、`反編譯逆向SOP.md`、`ARCH_DDD.md`、`tools/README.md`、`tools/doc_sync_check.py`、`.githooks/pre-commit`、`.githooks/commit-msg`、`packet_archive.py`、`testbot.py`；MapleForge `docs/README.md`、`docs/specs/*`、`docs/design/*`、`tools/`、`src/*.csproj`。指定的 `MapleForge\CLAUDE.md` 目前不存在。

## 結論摘要

黃逸最值得 MapleForge 立刻拿回來的不是某段 Python 程式，而是三個工程制度：

1. **Doc-Sync Gate**：把「改 code 後補活文件」從提醒升級成 git commit 前置條件。MapleForge 已有大量任務歷程與設計文件，但沒有硬閘門，這是 P0。
2. **工具知識資產化**：`packet_archive.py` 把一次性 pcapng 轉成 SQLite 可查詢資料庫；`testbot.py` 把 GUI smoke 變成封包級 headless bot。MapleForge 已有 `PacketDecoder`、`HeadlessClient`、windower capture，下一步應補「封包 archive DB + 查詢/匯出」和「headless bot 動作庫」。
3. **DDD 邊界明文化**：黃逸的 8 bounded contexts 可作為 MapleForge 子系統切分表，但不能照搬 Python 目錄。MapleForge 已有 `Core/Application/Adapters.V113/Net/Persistence/Content/Scripting`，應在既有 .NET solution 內落地 context，而不是重開一套 `domain/application/interface` 目錄。

## 採用清單

| 值得取經的點 | MapleForge 現況有/無 | .NET 落地做法 | 優先度 |
|---|---|---|---|
| Doc-Sync Gate 四問：機制、DB schema、協定格式、評審是否同步 | **無硬閘門**。有 `docs/specs/v113-protocol-spec.md`、`docs/design/*`、`docs/devlog/任務歷程/*`，但靠人記得。 | 新增 `tools/doc-sync/DocSyncCheck` 或 `tools/doc_sync_check.py`，分析 `git diff --cached`。文件映射：協定→`docs/specs/v113-protocol-spec.md`；世界/機制→`docs/design/in-game-執行期狀態架構.md` 或子系統設計；DB/持久→新增/指定 `docs/specs/persistence-model.md`；評審→`docs/devlog/任務歷程/*.md` 或 `docs/devlog/進度日誌.md`。 | P0 |
| git pre-commit + commit-msg trailer 雙 gate | **無**。MapleForge 是 git repo，但目前沒有 `.githooks`。 | `.githooks/pre-commit` 執行 checker；`.githooks/commit-msg` 驗證 `Doc-Sync: 世界[✓|NA] DB[✓|NA] 協定[✓|NA] 評審[✓|NA]`。啟用：`git config core.hooksPath .githooks`。 | P0 |
| Doc-Sync audit 模式 | **無**。有 devlog，但沒有用 commit range 檢查漏補。 | checker 支援 `--audit --since <ref>`，跑 `git diff <ref>..HEAD`。每 5-10 commit 或使用者抓漏時跑 full audit。 | P1 |
| 封包永久存檔 DB（`packet_archive.py`） | **部分有**。MapleForge 有 windower NDJSON capture、`Maple.Tools.PacketDecoder`、`HeadlessClient` catalog JSON，但沒有 SQLite archive/query。 | 新增 `tools/Maple.Tools.PacketArchive` CLI，讀 windower NDJSON/decoded packet，寫 SQLite：`captures(label, source, version, server, ingested_at, n_packets)`、`packets(capture, seq, dir, opcode, len, plain_hex, verified, confidence, notes)`。指令：`ingest`、`captures`、`query --opcode 0x013C`、`stats`、`export-fixture`。 | P0 |
| Headless bot 動作庫（黃逸 `testbot.py`） | **部分有**。`Maple.Tools.HeadlessClient` 可 login→channel→SetField，會輸出 catalog；尚未形成「動作 API」如 move/chat/npc/item/combat。 | 在 `Maple.Tools.HeadlessClient` 抽 `Bot` 類：`LoginAsync`、`EnterChannelAsync`、`MoveAsync`、`SayAsync`、`TalkNpcAsync`、`MoveItemAsync`、`AttackAsync`。每移植 Java handler 就新增一個 bot action + smoke。 | P1 |
| 工具 README 分類：日常工具 vs 開發工具 | **弱**。`tools/` 有多個工具，但缺統一索引與「何時用哪個」。 | 新增 `tools/README.md`，分類：windower/capture、PacketDecoder、PacketArchive、HeadlessClient、oracle、analyzers、diagnostics。每工具列啟動命令、輸入/輸出、可否進 CI。 | P1 |
| 反編譯/靜態情報優先，側錄補洞 | **已部分採用，但 MapleForge 情境不同**。MapleForge 有 OdinMS Java 原碼作權威，且已有 Java 移植路線圖。 | 改寫為「Java 原碼 → 真客戶端 capture → 必要時客戶端反編譯」三層情報鏈。先讀 Java handler/packet creator；只有 Java 缺漏、TMS v113 客戶端特殊行為、或 UI/反作弊/啟動器問題才進 client reverse。 | P1 |
| 四層情報鏈：廣度表 → 流程推導 → 缺口清單 → 深度 disasm | **部分有**。MapleForge devlog 已有 handler gap、Java 移植路線圖，但流程/缺口和封包 archive 尚未制度化。 | 每個子系統開工前產生 `docs/devlog/任務歷程/YYYY...` 的固定區塊：Java 來源、opcode/packet 表、流程鏈、缺口清單、capture 需求、DoD。 | P1 |
| 8 bounded contexts 作為子系統地圖 | **部分有**。已有 Accounts、Characters、Maps/World、Inventory、NPC/Scripting；Combat/Social/Shop 尚未完整。 | 不搬目錄；用既有 .NET 專案承載 context：`Maple.Core.Accounts/Characters/World/Inventory/Combat/Social`，`Maple.Application.*` use cases，`Maple.Adapters.V113` parsers/builders。 | P1 |
| Domain 不認封包 bytes/opcode，Application 不建 packet | **方向已有**。北極星是 `Maple.Core` 零 V113 import，協定鎖 `Maple.Adapters.V113`；但 Core 目前也有 `PacketReader/PacketWriter` 這類工具型別，需留意別讓協定滲漏擴大。 | 將規則寫進 `docs/specs/conventions.md` + analyzer/import test：`Maple.Core` 禁 `Maple.Adapters.V113`、禁 opcode enum、禁 session/packet envelope；封包 parser/builder 只在 `Adapters.V113`。Domain 對外只發 domain event 或純 DTO。 | P0 |
| Domain event / presenter pipeline | **不足**。現況多由 handler 直接接 Application/Adapter/Net。 | P1 起針對戰鬥/AOI/掉落導入 domain event：`CharacterMoved`、`NpcConversationStarted`、`ItemMoved`、`MonsterDamaged`、`ItemDropped`。Application 收 event，Adapter presenter 轉 V113 packet；不要讓 Core 知道 `0x41`、`0x013C`。 | P1 |
| Repository mapping 必須同步資料文件 | **部分有**。LiteDB 文件模型在設計書與背包設計中描述，但沒有專門 schema 活文件。 | 建 `docs/specs/persistence-model.md`。`Maple.Persistence` 或 `Character.Items/Equips/Stats` 變動時 gate 要求更新。對 LiteDB 文件 schema 特別有價值。 | P0 |
| Roslyn analyzer 作架構 gate | **已有**。`MapleForge.Analyzers` 已強制 Core/Application static mutable field。 | 擴充 MF0002/MF0003：Core 禁引用 Adapter/Net/Persistence/Scripting；Application 禁引用 Adapter.V113；Adapter 可引用 Application/Core/Net。也可用 NetArchTest 寫 xUnit 架構測試。 | P1 |
| GUI 實機驅動工具（黃逸 `game.ps1` / `locate.py`） | **MapleForge 有另一套**。已有 windower、diag 腳本、Themida 限制下的使用者手動啟動 + server 端觀察。 | 不急著搬 OpenCV locate。若後續需要點 NPC/背包拖曳自動化，再做 `tools/Maple.Tools.ClientDriver` 或 PowerShell 包裝；優先 server-side headless + capture。 | P2 |
| 資源瀏覽器 / DB 瀏覽器 | **可能不足**。MapleForge 有 WZ data provider 與 LiteDB，但未見 web browser 工具。 | 戰鬥/道具/商店階段後做 `tools/Maple.Tools.ContentBrowser`（Blazor/Minimal API 或 console query），查 map/npc/mob/item/string；LiteDB 可做 `Maple.Tools.DbTool`。 | P2 |

## Doc-Sync Gate：MapleForge 能不能照搬？

可以照搬「制度」，不建議逐字照搬「規則與路徑」。

黃逸 `doc_sync_check.py` 的核心能力：

- 讀 `git diff --cached --name-status -z` 和每檔 staged diff。
- 依路徑與關鍵字判定需要同步的類別：世界、DB、協定、評審。
- 檢查對應文件是否也 staged。
- `pre-commit` 不通就 block。
- `commit-msg` 要求 trailer，且 required 類別不能填 `NA`；填 `✓` 必須真的 staged 對應文件。
- `--audit --since <ref>` 可審計已提交 range。

MapleForge 的映射要改：

| Gate 類別 | MapleForge 觸發 | 必須同步 |
|---|---|---|
| 世界/機制 | `src/Maple.Core/World`、`Maps`、`Inventory`、`Characters`、`Application/Maps`、`Application/Npc`、未來 `Combat/Social` 變動，或 Java handler 移植改變遊戲機制 | `docs/design/in-game-執行期狀態架構.md`、對應子系統設計檔、或當次 `docs/devlog/任務歷程/*.md` |
| DB/持久 | `src/Maple.Persistence`、`Character` 文件欄位、`ItemRecord`、LiteDB repository mapping、資料 migration | 建議新增 `docs/specs/persistence-model.md` |
| 協定 | `src/Maple.Adapters.V113` 的 opcode/parser/builder/packet encoder、`Maple.Net` framing/cipher 行為、`tools/Maple.Tools.PacketDecoder` | `docs/specs/v113-protocol-spec.md` |
| 評審 | 改 opcode byte、改 Java 權威結論、改 DB schema、跨 Core/Application/Adapters/Persistence 的大變更、推翻既有設計 | `docs/devlog/任務歷程/*.md` 的 review 段，或未來 `docs/reviews/*.md` |

### 最小可行 Python 骨架

這版可以先放 `tools/doc_sync_check.py`，以低成本上線；之後再轉成 C# CLI 也行。

```python
#!/usr/bin/env python3
# -*- coding: utf-8 -*-
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys

os.environ.setdefault("PYTHONUTF8", "1")

DOCS = {
    "世界": [
        "docs/design/in-game-執行期狀態架構.md",
        "docs/design/背包道具-領域分層設計.md",
    ],
    "DB": ["docs/specs/persistence-model.md"],
    "協定": ["docs/specs/v113-protocol-spec.md"],
    "評審": ["docs/devlog/進度日誌.md"],  # 或 docs/devlog/任務歷程/*.md
}

WORLD_RE = re.compile(r"(Move|Npc|Inventory|Item|Map|Field|Combat|Monster|Quest|Party|Chat|Portal|Buff)", re.I)
DB_RE = re.compile(r"(LiteDB|Bson|Repository|ItemRecord|Character\.|EnsureIndex|Upsert|Delete|Insert)", re.I)
PROTO_RE = re.compile(r"(opcode|packet|payload|cipher|iv|aes|ofb|header|offset|0x[0-9a-f]+|Write|Read)", re.I)

def git(*args: str) -> str:
    p = subprocess.run(["git", "-c", "core.quotepath=false", *args],
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace")
    if p.returncode:
        raise SystemExit(p.stderr.strip() or f"git {' '.join(args)} failed")
    return p.stdout

def staged_files() -> list[str]:
    raw = subprocess.run(["git", "-c", "core.quotepath=false", "diff", "--cached", "--name-only", "-z"],
                         stdout=subprocess.PIPE, check=True).stdout
    return [x.decode("utf-8", "replace").replace("\\", "/") for x in raw.split(b"\0") if x]

def staged_diff(path: str) -> str:
    return git("diff", "--cached", "--unified=0", "--", path)

def doc_staged(category: str, files: set[str]) -> bool:
    if category == "評審":
        return "docs/devlog/進度日誌.md" in files or any(f.startswith("docs/devlog/任務歷程/") and f.endswith(".md") for f in files)
    return any(d in files for d in DOCS[category])

def analyze() -> dict[str, list[str]]:
    files = set(staged_files())
    required: dict[str, list[str]] = {}
    for path in files:
        if path.startswith("docs/") or path.startswith("tests/"):
            continue
        diff = staged_diff(path)
        text = "\n".join(line[1:] for line in diff.splitlines()
                         if line.startswith(("+", "-")) and not line.startswith(("+++", "---")))

        if path.startswith(("src/Maple.Core/", "src/Maple.Application/")) and WORLD_RE.search(path + "\n" + text):
            required.setdefault("世界", []).append(path)
        if path.startswith(("src/Maple.Persistence/", "src/Maple.Core/Characters", "src/Maple.Core/Inventory")) and DB_RE.search(text):
            required.setdefault("DB", []).append(path)
        if path.startswith(("src/Maple.Adapters.V113/", "src/Maple.Net/", "tools/Maple.Tools.PacketDecoder/")) and PROTO_RE.search(path + "\n" + text):
            required.setdefault("協定", []).append(path)
        if len({p.split("/")[1] for p in files if p.startswith("src/")}) >= 3:
            required.setdefault("評審", []).append(path)
    return required

TRAILER_RE = re.compile(r"^Doc-Sync:\s*(.+)$", re.M)

def run_pre_commit() -> int:
    files = set(staged_files())
    required = analyze()
    missing = [c for c in required if not doc_staged(c, files)]
    if not missing:
        print("PASS Doc-Sync")
        return 0
    print("BLOCK Doc-Sync")
    for c in missing:
        print(f"- {c}: missing {', '.join(DOCS[c])}; touched {', '.join(required[c][:3])}")
    print("Add matching docs or unstage/revert the triggering code.")
    return 2

def run_commit_msg(path: str) -> int:
    msg = open(path, encoding="utf-8-sig", errors="replace").read()
    required = analyze()
    files = set(staged_files())
    m = TRAILER_RE.search(msg)
    if not m:
        print("BLOCK missing trailer: Doc-Sync: 世界[NA] DB[NA] 協定[NA] 評審[NA]")
        return 2
    trailer = m.group(1)
    blocked = False
    for c in ("世界", "DB", "協定", "評審"):
        if c in required and re.search(rf"{c}\s*\[?NA\]?", trailer, re.I):
            print(f"BLOCK {c} is required but trailer says NA")
            blocked = True
        if re.search(rf"{c}\s*\[?✓\]?", trailer) and not doc_staged(c, files):
            print(f"BLOCK {c} says ✓ but matching doc is not staged")
            blocked = True
    return 2 if blocked else 0

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--staged", action="store_true")
    ap.add_argument("--mode", choices=["pre-commit"])
    ap.add_argument("--commit-msg")
    args = ap.parse_args()
    if args.commit_msg:
        return run_commit_msg(args.commit_msg)
    if args.staged and args.mode == "pre-commit":
        return run_pre_commit()
    ap.error("use --staged --mode pre-commit or --commit-msg <file>")

if __name__ == "__main__":
    sys.exit(main())
```

### Git hooks 骨架

`.githooks/pre-commit`：

```sh
#!/bin/sh
export PYTHONUTF8=1
python tools/doc_sync_check.py --staged --mode pre-commit
exit $?
```

`.githooks/commit-msg`：

```sh
#!/bin/sh
export PYTHONUTF8=1
python tools/doc_sync_check.py --commit-msg "$1"
exit $?
```

啟用：

```powershell
git config core.hooksPath .githooks
```

commit trailer 範例：

```text
Doc-Sync: 世界[✓] DB[NA] 協定[✓] 評審[NA]
```

### C# CLI 方向

若想完全 .NET 化，建議新增 `tools/Maple.Tools.DocSyncCheck`：

- `System.CommandLine` 或簡單 `args` parser。
- `GitDiffProvider` 包 `git diff --cached --name-status -z`、`git diff --cached --unified=0 -- <path>`。
- `DocSyncAnalyzer` 回傳 `Requirement(Category, Reason, Paths)`.
- `CommitMessageValidator` 驗 `Doc-Sync` trailer。
- xUnit 測試以 fake staged diff 鎖規則。

但是 P0 不必等 C# CLI。Python 版足夠當第一道硬閘門，且可直接從黃逸規則改。

## DDD 架構移植建議

黃逸 `ARCH_DDD.md` 的可移植重點：

- Domain 純同步、純規則，不碰 IO、DB、TCP、Packet、bytes。
- Application 負責 use case、transaction、domain event dispatch，不解析封包、不建 bytes。
- Infrastructure 實作 repository、codec、static data、logging。
- Interface 負責 session、router、payload parser、packet builder、version adapter。
- 用 Command/Registry Router/Builder/Factory/Repository/State Machine/Pipeline/Adapter 等 pattern 拆掉 giant handler。

MapleForge 不應把這四層照字面改名，因為現有 solution 已經對應：

| 黃逸層 | MapleForge 對應 |
|---|---|
| Domain | `Maple.Core` |
| Application | `Maple.Application` |
| Infrastructure | `Maple.Net`、`Maple.Persistence`、`Maple.Content`、部分 `Maple.Scripting` |
| Interface | `Maple.Adapters.V113` + Host/connection handler 的 protocol adapter 部分 |

8 contexts 的對應：

| 黃逸 Context | MapleForge 對應與建議 |
|---|---|
| Account/Auth | 已有 `Core.Accounts`、`Application.Accounts`，保留。 |
| Character | 已有 `Core.Characters`、`Application.Characters`，保留，持久文件 schema 要納入 Doc-Sync。 |
| World & Scene | 對應 `Core.World`、`Core.Maps`、`Application.Maps`。建議補 `FieldEntered/FieldLeft/ObjectSpawned` domain events。 |
| Movement | MapleStory movement 不能獨立成太大 context；可放 `Core.World`/`Application.Maps`，Parser/encoder 在 `Adapters.V113`。 |
| Inventory | 已有 `Core.Inventory`，方向正確；穿脫裝、掉落、交易時需要事件和持久 schema gate。 |
| Shop | MapleStory 可先歸 `Application.Npcs` + `Core.Inventory/Economy`，等商城/商店複雜後再獨立。 |
| Combat | 尚未完整，建議獨立 `Core.Combat` + `Application.Combat`，Adapter 只轉 attack packet。 |
| Social | 尚未完整，建議 `Core.Social` + `Application.Social`，涉及 world server/party/guild/buddy 時再擴。 |

最重要的約束：**不要讓 `Maple.Core` import V113 或 opcode。** MapleForge 的北極星比黃逸更強，因為 MapleForge 有版本 adapter 目標。Domain event 可用於隔離：

```csharp
// Maple.Core
public sealed record ItemMoved(int CharacterId, byte InventoryType, short FromSlot, short ToSlot);
public sealed record MonsterDamaged(int FieldId, int AttackerId, int MonsterObjectId, int Damage, int RemainingHp);

// Maple.Application
public interface IDomainEventSink
{
    ValueTask PublishAsync(object domainEvent, CancellationToken ct);
}

// Maple.Adapters.V113
// event presenter: ItemMoved -> V113InventoryPackets.ModifyMove(...)
// event presenter: MonsterDamaged -> V113 combat damage packet
```

## 工具鏈落地建議

### P0：PacketArchive

MapleForge 已有 windower capture 和 `PacketStreamDecoder`，但資料仍散在 NDJSON、catalog JSON、devlog。建議新增封包 archive DB：

```text
tools/Maple.Tools.PacketArchive/
  Program.cs
  PacketArchiveDb.cs
  WindowerNdjsonIngest.cs
  DecodedPacketIngest.cs
  QueryCommand.cs
```

SQLite schema：

```sql
CREATE TABLE captures(
  label TEXT PRIMARY KEY,
  source TEXT NOT NULL,
  client_version TEXT NOT NULL,
  ingested_at TEXT NOT NULL,
  n_packets INTEGER NOT NULL,
  notes TEXT
);

CREATE TABLE packets(
  id INTEGER PRIMARY KEY,
  capture TEXT NOT NULL,
  seq INTEGER NOT NULL,
  dir TEXT NOT NULL,
  opcode INTEGER NOT NULL,
  len INTEGER NOT NULL,
  plain_hex TEXT NOT NULL,
  verified TEXT NOT NULL DEFAULT 'unverified',
  confidence TEXT NOT NULL DEFAULT 'unknown',
  notes TEXT
);

CREATE INDEX ix_packets_opcode ON packets(opcode);
CREATE INDEX ix_packets_capture_opcode ON packets(capture, opcode);
```

指令：

```powershell
dotnet run --project tools/Maple.Tools.PacketArchive -- ingest-windower tools/windower/captures/foo.ndjson --label login-ok
dotnet run --project tools/Maple.Tools.PacketArchive -- query --opcode 0x013C --dir s2c --limit 20
dotnet run --project tools/Maple.Tools.PacketArchive -- stats --label login-ok
dotnet run --project tools/Maple.Tools.PacketArchive -- export-fixture --opcode 0x007B --out tests/fixtures/set-field.json
```

### P1：HeadlessClient 從流程程式升級成 Bot API

`Program.cs` 現在是線性流程：login server → char select → channel → SetField。建議保留 CLI，但抽可重用 API：

```csharp
public sealed class MapleBot : IAsyncDisposable
{
    public Task LoginAsync(string account, string password, CancellationToken ct);
    public Task<int> SelectFirstCharacterAsync(CancellationToken ct);
    public Task EnterChannelAsync(int characterId, CancellationToken ct);
    public Task MoveAsync(short x, short y, CancellationToken ct);
    public Task SayAsync(string text, CancellationToken ct);
    public Task TalkNpcAsync(int objectId, byte mode, int selection, CancellationToken ct);
    public Task MoveItemAsync(byte inventoryType, short src, short dst, short quantity, CancellationToken ct);
}
```

每個 Java handler 移植完成就補一個 bot action，讓 smoke 從「進圖」逐步變成「走路、聊天、點 NPC、拖道具、打怪」。

### P1：tools/README.md

MapleForge 應補工具索引，避免工具知識散在 devlog：

```text
tools/
  windower/                  真客戶端 winsock capture / instrumentation
  Maple.Tools.PacketDecoder/ 解密與重組
  Maple.Tools.PacketArchive/ 封包知識庫
  Maple.Tools.HeadlessClient/封包級 bot/smoke
  oracle/                    Java golden vector
  MapleForge.Analyzers/      架構 gate
```

## 不該照搬的部分

1. **不要照搬黃逸 source-less 逆向優先級。** 黃逸需要從 UE2 `.u`、DLL exports、SendNNN/RCVNNN 命名規律建立 opcode 地圖；MapleForge 已有 OdinMS Java 原碼作 v113 權威。MapleForge 的第一情報源應是 Java handler/packet creator，client reverse 是補洞，不是主線。
2. **不要照搬 8-context 目錄樹。** MapleForge 已有 .NET solution 邊界與北極星；重開 `domain/application/interface` 只會製造雙架構。只取 context mapping 和依賴規則。
3. **不要照搬 Mongo repository 偵測規則。** 黃逸的 DB gate 看 `_to_doc/_from_doc/collection/$set/index`；MapleForge 用 LiteDB 文件模型，應偵測 `Bson`、`LiteDatabase`、repository、`Character` 文件欄位、`ItemRecord` 等。
4. **不要把 GUI locate/game.ps1 放到 P0。** MapleForge 遇到 Themida，自動啟動真客戶端不穩；目前更高價值是 server-side headless、windower capture、封包 archive。GUI 自動化等 NPC/背包視覺操作真的需要再做。
5. **不要讓 fixture 取代設計真相。** MapleForge 已在 `封包擷取模式-設計.md` 寫明 fixture 不是 domain truth。封包 archive 只保存證據；設計真相仍在 Java 權威、Core domain、protocol spec。
6. **不要在 Doc-Sync Gate 初版過度嚴苛。** P0 先保護協定、DB、世界機制、評審四類；低風險測試、logging、純工具 UI 只提示不 block。否則會讓開發者繞過 hook。

## 建議落地順序

1. **P0-1：Doc-Sync Gate MVP**  
   新增 `tools/doc_sync_check.py` + `.githooks/pre-commit` + `.githooks/commit-msg`；先支援 staged diff、四類 mapping、trailer。啟用前用 3-5 個近期 commit 模擬，調整誤判。

2. **P0-2：Persistence 活文件**  
   新增 `docs/specs/persistence-model.md`，把 LiteDB `Account/Character/Items/Equips/Stats` 文件形狀寫清楚，讓 DB gate 有落點。

3. **P0-3：PacketArchive**  
   把 windower capture / decoded catalog 寫入 SQLite，先支援 `query` 和 `stats`。後續再接 `export-fixture`。

4. **P1-1：架構 analyzer 擴充**  
   在現有 MF0001 之外補「Core/Application 禁止 Adapter.V113 反向依賴」與「Core 禁 opcode/packet envelope」規則，或先用 xUnit architecture tests。

5. **P1-2：Headless bot API**  
   從 `Maple.Tools.HeadlessClient` 抽 `MapleBot`，每移植一個 Java handler 補一個 smoke action。

6. **P1-3：DDD context event 化**  
   從高風險系統開始：Combat、Inventory、Field/AOI。先補 domain event 和 presenter，不做大重構。

## 最終判斷

黃逸的制度成熟度高於 MapleForge；MapleForge 的程式分層與測試資產則已經有 .NET 基礎。最佳策略是：**Doc-Sync Gate 和 PacketArchive 直接取制度；DDD 取邊界規則與 context mapping；反編譯 SOP 改寫成 Java-first 情報鏈。** 這樣能補 MapleForge 目前最脆弱的「文件同步與逆向證據保存」，又不破壞既有 `Maple.Core` / `Maple.Adapters.V113` 北極星。
