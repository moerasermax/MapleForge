# Task: Port NOTE_ACTION (1 opcode) to MapleForge

You are porting the Note/Message (留言) system from a Java OdinMS v113 MapleStory server to a C# .NET 10 MapleForge framework.

## Opcode to port
- NOTE_ACTION (recv 0x7B) — type byte: 0=send note, 1=delete notes

## Architecture rules
- **Core** (`src/Maple.Core/`) — domain models, zero protocol imports
- **Application** (`src/Maple.Application/`) — use-case services
- **Persistence** (`src/Maple.Persistence/`) — repository implementations
- **Adapters.V113** (`src/Maple.Adapters.V113/Channel/`) — protocol-specific handlers & packets

## What to create

### 1. Core domain (`src/Maple.Core/Social/`)
- `Note.cs` — record: Id (int), SenderName (string), ReceiverName (string), Message (string), Fame (int, 0 or 1), Timestamp (long), Read (bool)
- `INoteRepository.cs` — interface: GetNotesForCharacterAsync(name), AddNoteAsync(note), DeleteNoteAsync(id)

### 2. Application service (`src/Maple.Application/Social/`)
- `NoteService.cs`:
  - SendNote(senderName, receiverName, message, fame) → success/fail
  - GetNotes(characterName) → list
  - DeleteNote(noteId, gain fame?)
  - Simple validation (non-empty name/message)

### 3. Persistence (`src/Maple.Persistence/Notes/`)
- `LiteDbNoteRepository.cs` — LiteDB implementation
  - Collection "notes", index on ReceiverName

### 4. Adapters (`src/Maple.Adapters.V113/Channel/`)
- `V113NoteHandler.cs` — handler with `HandleNoteActionAsync`:
  - type 0 (send): read name(string), message(string), fame(byte>0?), skip int(0), read long (cashId)
    - For MVP: just send the note without the cash item validation (the cash item part requires CashInventory which isn't fully integrated)
  - type 1 (delete): read count(byte), skip 2 bytes, then loop: read id(int) + read byte(>0 = gain fame)
- `V113NotePackets.cs` — packet encoding:
  - SHOW_NOTES (send 0x26) — showNotes(list of notes): count byte, then for each: id(int), sender(string), message(string), timestamp(long), fame(byte)

### 4. Tests (`tests/Maple.Adapters.V113.Tests/`)
- `NoteHandlerTests.cs` — at least 2 tests: send note parse, show notes packet encoding

## Java reference files
Read these for the exact logic:
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\handling\channel\handler\PlayersHandler.java` — lines 50-82 (`Note` method)
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\tools\MaplePacketCreator.java` — search for `showNotes` or `SHOW_NOTES`
- Send opcode from send.properties: `SHOW_NOTES = 0x26`

## Existing code to reference for patterns
- `src/Maple.Core/Guilds/IGuildRepository.cs` — repository interface pattern
- `src/Maple.Persistence/Guilds/LiteDbGuildRepository.cs` — LiteDB implementation pattern
- `src/Maple.Adapters.V113/Channel/V113DueyPackets.cs` — similar handler pattern for Duey (mail service)

## Important
- Do NOT modify `V113ChannelConnectionHandler.cs` — I will wire the dispatch myself
- Do NOT modify `V113ChannelOpcodes.cs` — already has the constants
- Create NEW files only
- Use `PacketWriter`/`PacketReader` from `Maple.Core.IO`
- xunit for tests
- No comments unless the WHY is non-obvious
