# MapleForge 命名與架構規範

## Namespace 職責

| Namespace | 職責 |
|-----------|------|
| `Maple.Core` | 純領域模型、介面、Value Objects（零 I/O、零 static 可變狀態） |
| `Maple.Application` | Use cases、Service、Handler |
| `Maple.Net` | TCP session、listener、cipher framing |
| `Maple.Versioning` | Cipher 抽象介面（`IPacketCipher`、`IVersionCipherFactory`） |
| `Maple.Adapters.V113` | v113 特有實作（crypto、opcodes、packets） |
| `Maple.Persistence` | LiteDB/Mongo repository 實作 |
| `Maple.Content` | WZ reader、地圖/資料提供者 |
| `Maple.Scripting` | 腳本引擎整合 |
| `Maple.Host.Shared` | DI 組裝、`ServerInstanceOptions` |
| `Maple.Host.Login` / `Maple.Host.Channel` | 進入點 |

## 資料夾慣例

- 每個 namespace 一個資料夾，子功能用子資料夾
- 測試放 `tests/` 平行於 `src/`
- 工具放 `tools/`

## 命名慣例

- 介面：`I` 前綴（`IAccountRepository`）
- Async 方法：`Async` 後綴
- v113 封包類：`V113` 前綴（`V113LoginPackets`）
- Opcode 常數：`V113RecvOp` / `V113SendOp`
- 設定類：`Options` 後綴（`MapleDatabaseOptions`）

## 依賴方向

```
Host → Adapters → Application → Core
Host → Net/Persistence/Content → Core
```

## 禁止事項（MF0001 強制）

- `Maple.Core` 和 `Maple.Application` 禁止 static 可變欄位
- 任何層禁止反向依賴（Core 不能 reference Application）
