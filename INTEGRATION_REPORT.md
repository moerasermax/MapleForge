# Batch-5 Integration Report

Date: 2026-06-12

## Scope

Integrated the 7 batch-5 systems already copied into the master worktree:

- reactor
- trade
- duey
- guild bbs
- ring-follow
- npc-services
- buff-items

No files were pulled from other branches. The item-use system is intentionally out of scope for this integration.

## Dispatch Wiring

Active recv opcodes wired in `V113ChannelConnectionHandler`:

- reactor: `DamageReactor=0xC9`, `TouchReactor=0xCA`
- trade: `PlayerInteraction=0x73` routed to `V113PlayerInteractionRouter` trade branch only
- duey: `DueyAction=0x3B`
- bbs: `BbsOperation=0x94`
- ring: `RingAction=0x81`
- npc-services/owl: `Owl=0x3C`, `OwlWarp=0x3D`, `UseOwlMinerva=0x4D`
- buff-items: `Solomon=0x9B`, `GachExp=0x9C`, `TransformPlayer=0xA0`, `XmasSurprise=0xA2`

Disabled / not dispatched:

- `FOLLOW_REQUEST` / `FOLLOW_REPLY`: Java `recv.properties` comments them out; `FOLLOW_REPLY` candidate `0x7A` conflicts with active `BuddyListModify`. Handler/packets/tests remain compiled.
- `REPAIR` / `REPAIR_ALL`: Java `recv.properties` comments them out; legacy candidates `0x73`/`0x72` conflict with active `PlayerInteraction`/`Messenger`. Handler/packets/tests remain compiled.
- Owl cash-item route for `5230000`: there is no existing `USE_CASH_ITEM` inventory route in master to hook cleanly. Active Owl opcodes are wired; cash-item route remains TODO.

## Opcode Constants

Added shared recv/send constants in `V113ChannelOpcodes.cs` after checking the existing table and Java properties for name/value collisions.

Send constants added include reactor `0x113/0x115/0x116`, duey `0x155`, trade `0x146`, bbs `0x68`, xmas `0x161`, marriage `0x41/0x42/0x62`, `ShowForeignEffect=0xBF`, `RepairWindow=0xD5`, `ShopScannerResult=0x3F`, and `ShopLinkResult=0x40`.

## DI

Registered in `MapleServerHost`:

- Duey: `AddMapleDueyPersistence`, `DueyService`, `V113DueyHandler`
- BBS: `AddGuildBbsPersistence`, `GuildBbsService`, `V113BbsHandler`
- Reactor: `ReactorScriptOptions`, `IReactorScriptFactory -> JintReactorScriptFactory`, `ReactorService`
- Trade: `TradeService`, `V113PlayerInteractionRouter`
- Ring/follow: `IOnlinePlayerRuntimeRegistry -> InMemoryOnlinePlayerRuntimeRegistry`, `RingService`, `FollowService`, `V113RingHandler`, `V113FollowHandler`
- NPC item services: `IEquipRepairCatalog -> EmptyEquipRepairCatalog`, `EquipRepairService`, `IOwlSearchCatalog -> EmptyOwlSearchCatalog`, `OwlService`, `V113RepairHandler`, `V113OwlHandler`
- Buff-items: `IV113XmasSurpriseRewardSource -> V113XmasSurpriseRewardSource`, `V113BuffItemHandler`

## Runtime Hooks

- Registered both runtime player registries after `_onlinePlayers.Register(...)`:
  - `TradeService.RegisterPlayer(...)`
  - `IOnlinePlayerRuntimeRegistry.Register(...)`
- Logout cleanup:
  - `TradeService.DeregisterPlayer(...)` plus trade cancel notice dispatch
  - `FollowService.CancelFollow(...)`
  - `IOnlinePlayerRuntimeRegistry.Deregister(...)`
- Kept both registries for this batch. Unifying trade/runtime registries is left as technical debt.

## Reactor

- Promoted `FieldObjectType.Reactor` into `IFieldObject.cs`.
- Updated `Reactor.Type` to return the formal enum member.
- Kept `ReactorFieldObjectTypes.Reactor` as a compatibility constant pointing to `FieldObjectType.Reactor`.
- `EnterField` initializes map reactors after monster spawn on first field creation.
- Player login and `WarpAsync` replay field reactors with `V113ReactorHandler.SendFieldReactorsAsync`.

## Minimal Adjustments

- No handler/service method signatures were changed.
- `Reactor.cs` was minimally adjusted to use the formal `FieldObjectType.Reactor` enum member.

## Verification

- `dotnet build MapleForge.slnx -v minimal --nologo`: 0 warnings, 0 errors
- `dotnet test tests/Maple.Core.Tests/Maple.Core.Tests.csproj --no-restore -v minimal --nologo`: 61/61 passed
- `dotnet test tests/Maple.Application.Tests/Maple.Application.Tests.csproj --no-restore -v minimal --nologo`: 114/114 passed
- `dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj --no-restore -v minimal --nologo`: 214/214 passed
- `dotnet test tests/Maple.Persistence.Tests/Maple.Persistence.Tests.csproj --no-restore -v minimal --nologo`: 6/6 passed
- `dotnet test tests/Maple.Net.Tests/Maple.Net.Tests.csproj --no-restore -v minimal --nologo`: 2/2 passed
- `git diff --check`: no whitespace errors; only CRLF conversion warnings

## Remaining TODO

- Add a real `USE_CASH_ITEM` route and wire Owl cash item `5230000`.
- Unify trade runtime registry with `IOnlinePlayerRuntimeRegistry`.
- Verify and enable follow opcodes only after true-client capture resolves disabled/conflicting candidates.
- Verify and enable repair opcodes only after true-client capture resolves disabled/conflicting candidates.
- Replace empty repair/Owl catalogs with real item durability and hired-merchant/MTS data.
- Add cash catalog entries for Xmas surprise box/reward serials if content parity is required.
- Run true v113 client smoke for these systems; current evidence is build/unit/headless only.
