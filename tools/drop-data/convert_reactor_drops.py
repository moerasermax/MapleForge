#!/usr/bin/env python3
"""P182：把 Java 舊伺服器 SQL dump 的 `reactordrops` 表轉成 MapleForge 的反應堆掉落 JSON。

用法：
    python3 tools/drop-data/convert_reactor_drops.py <testms.sql> <輸出.json>

輸出格式（JsonReactorDropCatalog）：
    {"source": "reactordrops", "drops": {"<reactorId>": [[itemId, chance, questId], ...]}}

輸出檔是遊戲資料，不進版控；伺服器以 Content:ReactorDropCatalogPath 指定路徑。
欄位順序對照 Java 表結構：reactordropid, reactorid, itemid, chance, questid。
"""
import json
import re
import sys

ROW = re.compile(r"INSERT INTO `reactordrops` VALUES \(([^)]*)\);")


def convert(sql_path: str) -> dict:
    rows: list[tuple[int, int, int, int, int]] = []
    with open(sql_path, encoding="utf-8", errors="replace") as sql:
        for line in sql:
            match = ROW.match(line.strip())
            if match:
                rows.append(tuple(int(v) for v in match.group(1).split(",")))
    drops: dict[str, list[list[int]]] = {}
    for _id, reactor, item, chance, quest in sorted(rows):
        drops.setdefault(str(reactor), []).append([item, chance, quest])
    return {"source": "reactordrops", "drops": drops}


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    document = convert(sys.argv[1])
    with open(sys.argv[2], "w", encoding="utf-8") as out:
        json.dump(document, out, separators=(",", ":"))
    rows = sum(len(v) for v in document["drops"].values())
    print(f"{len(document['drops'])} reactors, {rows} rows -> {sys.argv[2]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
