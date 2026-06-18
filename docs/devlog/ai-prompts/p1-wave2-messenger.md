# Task: Port Messenger System (1 opcode) to MapleForge

You are porting the Messenger (信差) system from a Java OdinMS v113 MapleStory server to a C# .NET 10 MapleForge framework.

## Opcode to port
- MESSENGER (recv 0x72) — sub-op byte: open(0x00, create or join)/exit(0x02)/invite(0x03)/decline(0x05)/message(0x06)

## Architecture rules
- **Core** (`src/Maple.Core/`) — domain models, zero protocol imports
- **Application** (`src/Maple.Application/`) — use-case services
- **Adapters.V113** (`src/Maple.Adapters.V113/Channel/`) — protocol-specific handlers & packets

## What to create

### 1. Core domain (`src/Maple.Core/Social/`)
- `Messenger.cs` — class: Id, Members (array of MessengerMember, max 3 slots), GetLowestPosition()
- `MessengerMember.cs` — record: CharacterId, Name, ChannelIndex, Position

### 2. Application service (`src/Maple.Application/Social/`)
- `MessengerService.cs` — in-memory registry + business logic:
  - CreateMessenger(member) → Messenger
  - JoinMessenger(messengerId, member) → success/fail
  - LeaveMessenger(messengerId, characterId)
  - GetMessenger(id) → Messenger?
  - Thread-safe

### 3. Adapters (`src/Maple.Adapters.V113/Channel/`)
- `V113MessengerHandler.cs` — handler class with `HandleMessengerAsync`
  - Parse sub-op byte, dispatch
  - Needs session hook interface to send packets to other players and find players by name
- `V113MessengerPackets.cs` — packet encoding:
  - Send opcode = 0x145 (MESSENGER)
  - messengerInvite(from, messengerId)
  - addMessengerPlayer(from, position, channel)
  - removeMessengerPlayer(position)
  - messengerChat(text)
  - messengerNote(text, mode, mode2)

### 4. Tests (`tests/Maple.Adapters.V113.Tests/`)
- `MessengerHandlerTests.cs` — at least 3 tests: create, join, chat

## Java reference files
Read these for the exact logic:
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\handling\channel\handler\ChatHandler.java` — lines 211-301 (`Messenger` method)
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\handling\world\MapleMessenger.java` (144 lines)
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\handling\world\MapleMessengerCharacter.java` (94 lines)
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\tools\MaplePacketCreator.java` — search for `messengerInvite`, `addMessengerPlayer`, `removeMessengerPlayer`, `messengerChat`, `messengerNote`
- Send opcode from send.properties: `MESSENGER = 0x145`

## Existing code to reference for patterns
- `src/Maple.Application/Parties/PartyService.cs` — in-memory registry pattern
- `src/Maple.Adapters.V113/Channel/V113PartyOperationHandler.cs` — handler with session hook pattern
- `src/Maple.Adapters.V113/Channel/V113ChatPackets.cs` — chat packet patterns

## Important
- Do NOT modify `V113ChannelConnectionHandler.cs` — I will wire the dispatch myself
- Do NOT modify `V113ChannelOpcodes.cs` — already has the constants
- Create NEW files only
- Use `PacketWriter`/`PacketReader` from `Maple.Core.IO`
- xunit for tests
- No comments unless the WHY is non-obvious
