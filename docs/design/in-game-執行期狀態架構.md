# in-game 執行期玩家狀態架構（團隊決策）

> 2026-06-02 團隊會議（claude-ultra + agy）決策，Opus 綜整。撐整個 in-game 移植階段。

## 決策

執行期玩家狀態（位置/stance/foothold、即時 HP/MP、buff、地圖）**獨立於持久 `Character`**，用**組合**（持有 Character）非繼承/擴充。**富領域實體放 Core，生命週期/session 綁定放 Application，傳輸留 Net、絕不進 Core。**

理由（採 claude-ultra 立場）：戰鬥傷害/範圍命中/死亡判定＝**遊戲規則＝領域邏輯**，故位置/血量要被規則讀寫→屬 Core。若放 Application 會使 Core 貧血、分層崩。`Character` 是 LiteDB 文件根（Load/Save 原子），不可被每 tick 變動污染。

## 物件歸屬

| 層 | 物件 | 角色 |
|---|---|---|
| **Core/World** | `Position`(record struct X/Y/Stance/Foothold) | 值物件 |
| Core/World | `IFieldObject`(ObjectId/Position/Type) | 地圖物件共同介面(玩家/怪/NPC/掉落鋪路) |
| Core/World | `Player`(持有 `Character` + `Position` + 行為 MoveTo…;之後 Vitals/Buff) | 入世實體,實作 IFieldObject |
| Core/World | `FieldInstance`(IFieldObject 集合;Add/Remove/GetPlayers/ObjectsInRange) | 執行期空間聚合(＝OdinMS MapleMap 乾淨版,每頻道一份) |
| (未來)Core | `FieldData` | 靜態地圖資料(foothold/portal/spawn,唯讀) |
| **Application/World** | `PlayerSession` | session-scoped 黏合(sessionId/channel/player/當前 Field),生命週期＝連線 |
| Application | `IFieldRegistry` | mapId→FieldInstance(取代/重構 `IMapSessionRegistry`) |
| Application | 送包 sink map | charId→Func<byte[],ct,Task>,**獨立於領域**(把 transport 從 MapPlayerEntry 拆出) |
| **Net** | `MapleSession` | 不動,純傳輸 |
| **Adapters.V113** | handler/parser | 變薄;`V113MovementParser` 輸出映射成 Core `Position`(別讓 Commands 等協定雜訊進 Core) |

## 資料流（移動）

Adapter 解析 bytes→Position end → Application 用例 `PlayerMovement.Apply(charId, end, rawBytes)` → 由 registry 取 Player → `player.MoveTo(end)`(Core) → 取同 Field 其他人 → Adapter 編碼 MOVE 廣播 → Net 送。**雙軌**：①原始 movement blob 原樣轉發(給別人客戶端平滑補間)②parser 抽的 Position 餵 server 權威模型(戰鬥/NPC/範圍/反作弊)。唯一位置真相＝Core `Player.Position`。

## 風險（團隊共識，按殺傷力）

1. **共享 Field 並行**：Player/Field 變跨 session 共享可變→建議每 FieldInstance 單執行緒命令/tick 佇列(field-actor),領域變更無鎖;**勿把 ConcurrentDictionary 灑進 Core 領域**。
2. **幽靈玩家/生命週期洩漏**(本機易崩):每條退出路徑都要從 Field 移除+存檔,綁死 MapleSession 生命週期。
3. **分層滲漏誘惑**:絕不給 Core `Player` 加 SendAsync/session ref;出站一律 Application→registry→Adapter 編碼→Net。現有 `MapPlayerEntry` 已犯一半(摻 SendPacket)→重構拆乾淨。
4. **雙真相漂移**:Player 在線唯一權威;Character 只在存檔/登出/換圖/checkpoint 由 Player 回寫;session 中途不從 Character 讀即時值。
5. **物件 id 空間**:Character.Id ≠ 地圖物件 id;現在就引入 IFieldObject.ObjectId + 每-Field id 配發器(玩家/怪/NPC/掉落統一可鎖定/範圍命中)。
6. **過度設計**:先最小富領域切片(Position→Player+FieldInstance→接 parser→ObjectsInRange),其餘藏介面後。

## 落地順序

1. ✅(本單元) Core/World：`Position`/`IFieldObject`/`Player`(先 Position+包 Character)/`FieldInstance`(Add/Remove/GetPlayers/ObjectsInRange) + Core 單元測試。
2. `V113MovementParser` 輸出映射 Core `Position`。
3. 重構 `IMapSessionRegistry`→`IFieldRegistry`(領域)+sink map(傳輸)；新增 `PlayerMovement.Apply` 用例。
4. handler 抽掉 stack ref，改呼用例；廣播維持原始 blob。
5. 並行模型(field-actor) 視需要引入。
