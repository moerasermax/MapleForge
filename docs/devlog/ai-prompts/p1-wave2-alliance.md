# Task: Port Alliance System (2 opcodes) to MapleForge

You are porting the Alliance (聯盟) system from a Java OdinMS v113 MapleStory server to a C# .NET 10 MapleForge framework.

## Opcodes to port
- ALLIANCE_OPERATION (recv 0x86) — sub-op switch: load(1)/invite(3)/accept(4)/leave(2)/expel(6)/changeLeader(7)/titleUpdate(8)/rankChange(9)/noticeUpdate(10)
- DENY_ALLIANCE_REQUEST (recv 0x87) — deny invitation

## Architecture rules
- **Core** (`src/Maple.Core/`) — domain models, zero protocol imports
- **Application** (`src/Maple.Application/`) — use-case services
- **Adapters.V113** (`src/Maple.Adapters.V113/Channel/`) — protocol-specific handlers & packets
- Follow existing patterns in `src/Maple.Core/Guilds/` and `src/Maple.Application/Guilds/GuildService.cs`

## What to create

### 1. Core domain (`src/Maple.Core/Alliances/`)
- `Alliance.cs` — record/class with: Id, Name, LeaderId, Notice, Ranks (string[5]), GuildIds (list, max 5), Capacity
- `IAllianceRepository.cs` — interface: FindByIdAsync, SaveAsync, DeleteAsync

### 2. Application service (`src/Maple.Application/Alliances/`)
- `AllianceService.cs` — in-memory registry + business logic:
  - CreateAlliance, AddGuild, RemoveGuild, ChangeLeader, UpdateRanks, UpdateNotice, ChangeAllianceRank
  - GetAllianceInfo (returns data for the load op)
  - Thread-safe (lock or ConcurrentDictionary)

### 3. Adapters (`src/Maple.Adapters.V113/Channel/`)
- `V113AllianceHandler.cs` — handler class with `HandleAllianceOperationAsync` and `HandleDenyAllianceRequestAsync`
  - Parse sub-op byte, dispatch to service
  - Return packets to send
- `V113AlliancePackets.cs` — packet encoding:
  - Send opcode = 0x3B (ALLIANCE_OPERATION)
  - AllianceInfo, AllianceUpdate, AllianceInvite, etc.

### 4. Tests (`tests/Maple.Adapters.V113.Tests/`)
- `AllianceHandlerTests.cs` — at least 3 tests: create, add guild, deny

## Java reference files
Read these for the exact logic:
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\handling\channel\handler\AllianceHandler.java` (167 lines)
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\handling\SendPacketOpcode.java` — ALLIANCE_OPERATION = 0x3B

## Existing code to reference for patterns
- `src/Maple.Core/Guilds/Guild.cs` — Guild has `AllianceId` field already
- `src/Maple.Application/Guilds/GuildService.cs` — GuildService pattern
- `src/Maple.Adapters.V113/Channel/V113GuildOperationHandler.cs` — handler pattern
- `src/Maple.Adapters.V113/Channel/V113GuildPackets.cs` — packet encoding pattern
- `src/Maple.Core/Characters/Character.cs` — Character has AllianceRank field

## Important
- Do NOT modify `V113ChannelConnectionHandler.cs` — I will wire the dispatch myself
- Do NOT modify `V113ChannelOpcodes.cs` — already has the constants
- Create NEW files only
- Use `PacketWriter`/`PacketReader` from `Maple.Core.IO`
- xunit for tests
- No comments unless the WHY is non-obvious
