# MapleForge batch-1 integration report

Date: 2026-06-06
Base master HEAD before integration: `64e8610`

## Merge result

- The five `port/*` branch refs initially still pointed at `b4e11f9`; their completed source/test changes were present as dirty worktree changes.
- I committed only source/test changes in each port worktree, leaving each worktree `PORT_REPORT.md` and `.codexdone` untracked there.
- Merged into master with `--no-ff` in the requested order:
  - `port/equip` -> merge commit `8d39992`
  - `port/mongo` -> merge commit `5169708`
  - `port/shop` -> merge commit `e632f39`
  - `port/storage` -> merge commit `922cf77`
  - `port/combat` -> merge commit `0a40e98`

## Conflicts resolved

- `src/Maple.Application/Npc/NpcContext.cs`
  - Kept both `PendingShop` and `PendingStorageNpcId`.
  - `ClearPending()` now clears dialog, warp, shop, and storage pending state.
  - `cm` context supports both `OpenShop(int)` and `OpenStorage()` / `SendStorage()`.
- `src/Maple.Application/Npc/NpcConversation.cs`
  - Constructor now accepts both optional `openShop` and `openStorage` delegates.
  - `FlushAsync()` handles dialog, shop, warp, and storage pending actions.
- `src/Maple.Core/Inventory/Inventory.cs`
  - Auto-merged cleanly; verified equip's `Contains`, `TryTake(slot)`, `TryPut(slot,item)` and storage's `TryTake(slot,quantity,...)` are all present.
- `src/Maple.Scripting/JintNpcScriptFactory.cs`
  - Auto-merged cleanly; verified `cm` exposes `openShop`, `openStorage`, and `sendStorage`.
- `MapleForge.slnx`
  - No merge conflict occurred, but I added both new test projects: `Maple.Core.Tests` and `Maple.Persistence.Tests`.

## Central wiring completed

- Added central v113 recv/send opcode constants for item move, shop, storage, close/ranged/magic attack placeholders, update stats, shop responses, storage, and monster spawn/kill/control/damage/move.
- Replaced `ITEM_MOVE` dispatch with `V113InventoryMoveHandler.ApplyItemMove(...)`, including equip/unequip negative slot routing.
- Added `NPC_SHOP (0x36)` dispatch to `ShopService` buy/sell/recharge handling and v113 shop response packets.
- Added `STORAGE (0x37)` dispatch to `StorageService` take-out/store/arrange/meso/close handling.
- Added `CLOSE_RANGE_ATTACK (0x25)` dispatch to `CombatService`, broadcasting close-range attack, monster damage, and monster kill packets.
- Added account-id lookup to `IAccountRepository` and LiteDB/Mongo implementations so channel login can load `Account` by `Character.AccountId`.
- Channel login now calls `player.AttachStorage(account)` when the account exists; storage is flushed to account on storage close and disconnect.
- Channel disconnect now flushes player inventory/character state and account storage.
- NPC conversation creation now passes `openShop`, `openStorage`, and `warp` delegates.
- Added `IFieldInstanceRegistry` / `InMemoryFieldInstanceRegistry` for per-map runtime fields.
- Channel login/warp now gets or creates the field, spawns static map monsters once per field, and replays alive monster spawn/control packets to joining players.
- Host DI now registers:
  - `IShopCatalog -> JsonShopCatalog`
  - `ShopService`
  - `StorageService`
  - `CombatService`
  - `IFieldInstanceRegistry -> InMemoryFieldInstanceRegistry`
- Host persistence options now project Mongo settings from configuration:
  - `Persistence:Provider`
  - `Persistence:MongoConnectionString`
  - `Persistence:MongoDatabaseName`

## Verification

- `dotnet build MapleForge.slnx --nologo`
  - First run hit a SourceLink file lock in `MapleForge.Analyzers`.
  - After `dotnet build-server shutdown`, the same command passed: 0 warnings, 0 errors.
- `dotnet test tests/Maple.Core.Tests/Maple.Core.Tests.csproj --nologo`
  - Passed: 4/4
- `dotnet test tests/Maple.Application.Tests/Maple.Application.Tests.csproj --nologo`
  - Passed: 40/40
- `dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj --nologo`
  - Passed: 54/54
- `dotnet test tests/Maple.Content.Tests/Maple.Content.Tests.csproj --nologo`
  - Passed: 10/10
- `dotnet test tests/Maple.Persistence.Tests/Maple.Persistence.Tests.csproj --nologo`
  - Passed: 3/3
- Total affected tests passed: 111/111
- `git diff --check`
  - No whitespace errors; only Git CRLF normalization warnings.

## Notes

- No unresolved build or test failures remain.
- Unit tests do not require a real MongoDB server; `Maple.Persistence.Tests` uses EphemeralMongo.
- Host runtime now defaults to MongoDB provider. A real server launch needs a reachable MongoDB server or `Persistence:Provider=LiteDb`.
- Worktrees were not deleted.

