# Task: Port USE_DOOR (1 opcode) to MapleForge

You are porting the Mystic Door (傳送門) system from a Java OdinMS v113 MapleStory server to a C# .NET 10 MapleForge framework.

## Opcode to port
- USE_DOOR (recv 0x7D) — player uses an existing mystic door to warp between town portal and target location
  - Packet: readInt (ownerId) + readByte (mode: 0=target-to-town, 1=town-to-target... actually 0 in Java means backwarp)

## Architecture rules
- **Core** (`src/Maple.Core/`) — domain models, zero protocol imports
- **Application** (`src/Maple.Application/`) — use-case services
- **Adapters.V113** (`src/Maple.Adapters.V113/Channel/`) — protocol-specific handlers & packets

## What to create

### 1. Core domain (`src/Maple.Core/World/`)
- `Door.cs` — class:
  - OwnerId (int) — the character who cast Mystic Door
  - OwnerPartyId (int?) — party access control
  - TownMapId (int) — the town map where the town-side portal appears (from MapData.ReturnMap)
  - TownPortalPosition (x, y)
  - TargetMapId (int) — the field map where the door was cast
  - TargetPosition (x, y)
  - Implements IFieldObject (has ObjectId for the field)
- This is a runtime-only object (not persisted)

### 2. Application service (`src/Maple.Application/Maps/`)
- `DoorService.cs`:
  - CreateDoor(ownerId, partyId, targetMapId, targetPos, townMapId, townPortalPos) → Door
  - GetDoorByOwner(mapId, ownerId) → Door?
  - RemoveDoor(ownerId)
  - WarpThroughDoor(door, player, backwarp) — determines destination map+position based on mode
  - Thread-safe, in-memory

### 3. Adapters (`src/Maple.Adapters.V113/Channel/`)
- `V113DoorHandler.cs` — handler with `HandleUseDoorAsync`:
  - Parse ownerId + mode byte
  - Look up door by ownerId in current map
  - Determine destination (town or target based on mode)
  - Return warp result
- `V113DoorPackets.cs` — packet encoding:
  - SpawnDoor (send to map) — for future use when Mystic Door skill is cast
  - RemoveDoor (send to map) — for cleanup

### 4. Tests (`tests/Maple.Adapters.V113.Tests/`)
- `DoorHandlerTests.cs` — at least 2 tests: warp town-to-target, warp target-to-town

## Java reference files
Read these for the exact logic:
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\handling\channel\handler\PlayersHandler.java` — lines 129-140 (`UseDoor` method)
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\server\maps\MapleDoor.java` (173 lines)

## Existing code to reference for patterns
- `src/Maple.Core/World/Player.cs` — Player runtime model
- `src/Maple.Core/Maps/FieldInstance.cs` — field runtime (has Get, Add, Remove for IFieldObject)
- `src/Maple.Application/Maps/MapService.cs` — map service pattern

## Important notes
- The door CREATION is triggered by casting Mystic Door skill (SPECIAL_MOVE 0x55). That skill handler does NOT exist yet, so for now just build the domain + USE_DOOR handler. The skill handler will call DoorService.CreateDoor later.
- Do NOT modify `V113ChannelConnectionHandler.cs` — I will wire the dispatch myself
- Do NOT modify `V113ChannelOpcodes.cs` — already has the constants
- Create NEW files only
- Use `PacketWriter`/`PacketReader` from `Maple.Core.IO`
- xunit for tests
- No comments unless the WHY is non-obvious
