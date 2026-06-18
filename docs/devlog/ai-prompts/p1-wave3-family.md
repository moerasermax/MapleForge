# Task: Port Family System (9 opcodes) to MapleForge

You are porting the Family (家族) system from a Java OdinMS v113 MapleStory server to a C# .NET 10 MapleForge framework.

## Opcodes to port
| Opcode | 0x值 | Java method | Description |
|---|---|---|---|
| REQUEST_FAMILY | 0x88 | FamilyHandler.RequestFamily | Query family pedigree |
| OPEN_FAMILY | 0x89 | FamilyHandler.OpenFamily | Open family info window |
| FAMILY_OPERATION | 0x8A | FamilyHandler.FamilyOperation | Invite player to family |
| DELETE_JUNIOR | 0x8B | FamilyHandler.DeleteJunior | Kick junior |
| DELETE_SENIOR | 0x8C | FamilyHandler.DeleteSenior | Leave senior |
| ACCEPT_FAMILY | 0x8D | FamilyHandler.AcceptFamily | Accept/deny family invite |
| USE_FAMILY | 0x8E | FamilyHandler.UseFamily | Use family buff/teleport |
| FAMILY_PRECEPT | 0x8F | FamilyHandler.FamilyPrecept | Set family notice |
| FAMILY_SUMMON | 0x90 | FamilyHandler.FamilySummon | Accept/deny summon |

## Architecture rules
- **Core** (`src/Maple.Core/`) — domain models, zero protocol imports
- **Application** (`src/Maple.Application/`) — use-case services
- **Adapters.V113** (`src/Maple.Adapters.V113/Channel/`) — protocol-specific handlers & packets
- Follow existing patterns in `src/Maple.Core/Guilds/` and `src/Maple.Application/Guilds/GuildService.cs`

## What to create

### 1. Core domain (`src/Maple.Core/Families/`)
- `Family.cs` — class: Id (int), LeaderId (int), Notice (string), Members (dictionary of FamilyMember by characterId)
- `FamilyMember.cs` — class: CharacterId (int), Name (string), SeniorId (int), Junior1 (int), Junior2 (int), CurrentRep (int), TotalRep (int), Level (int), Job (int)
  - Methods: GetOnlineJuniors, SetJunior1/2, etc.
- `FamilyBuff.cs` — static catalog of buff entries (type 0-10):
  - type 0: teleport to family member
  - type 1: summon family member
  - type 2: +50% drop 15min, type 3: +50% exp 15min
  - type 4: family pedigree 6+ online → +100% drop+exp 30min
  - type 5: +100% drop 15min, type 6: +100% exp 15min
  - type 7: +100% drop 30min, type 8: +100% exp 30min
  - type 9: +100% drop party 30min, type 10: +100% exp party 30min
  - Each entry has: Type (int), RepCost (int), BuffType (string), Duration (int minutes)
- `IFamilyRepository.cs` — interface: FindByIdAsync, SaveAsync, DeleteAsync

### 2. Application service (`src/Maple.Application/Families/`)
- `FamilyService.cs` — in-memory registry + business logic:
  - CreateFamily(leaderId) → Family
  - InviteToFamily(inviterPlayer, targetPlayer) → result
  - AcceptInvite(inviterCharId, targetPlayer) → result
  - DeleteJunior(player, juniorId) → result
  - DeleteSenior(player) → result
  - UseFamilyBuff(player, buffType) → result
  - SetFamilyPrecept(player, notice) → result
  - HandleFamilySummon(player, accepted, summonerName) → result
  - GetFamilyInfo(characterId) → family info
  - GetFamilyPedigree(characterId) → pedigree data
  - SplitFamily(characterId) — handle tree split when member leaves
  - Thread-safe (lock based)
- `InMemoryFamilyRepository.cs` — ConcurrentDictionary implementation
- `IFamilyRegistry.cs` — runtime lookup: GetFamilyForCharacter, Register, etc.

### 3. Adapters (`src/Maple.Adapters.V113/Channel/`)
- `V113FamilyHandler.cs` — handler class with methods for each opcode
  - Needs session hook interface (IV113FamilySessionHook) for: finding online players, sending packets to other players
- `V113FamilyPackets.cs` — packet encoding using these send opcodes:
  - FAMILY_CHART_RESULT = 0x56 — getFamilyPedigree
  - FAMILY_INFO_RESULT = 0x57 — getFamilyInfo
  - FAMILY_RESULT = 0x58 — family result/error
  - FAMILY_JOIN_REQUEST = 0x59 — sendFamilyInvite
  - FAMILY_JUNIOR = 0x5A — sendFamilyJoinResponse  
  - FAMILY_JOIN_ACCEPTED = 0x5B — getSeniorMessage
  - FAMILY_PRIVILEGE_LIST = 0x5C — family buff list
  - FAMILY_FAMOUS_POINT_INC_RESULT = 0x5D — changeRep
  - FAMILY_NOTIFY_LOGIN_OR_LOGOUT = 0x5E — login/logout notify
  - FAMILY_SET_PRIVILEGE = 0x5F — set privilege
  - FAMILY_SUMMON_REQUEST = 0x60 — familySummonRequest

### 4. Tests (`tests/Maple.Adapters.V113.Tests/`)
- `FamilyHandlerTests.cs` — at least 5 tests covering:
  - Create family (invite + accept)
  - Delete junior
  - Delete senior
  - Use family buff
  - Family precept

## Java reference files to READ
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\handling\channel\handler\FamilyHandler.java` (371 lines) — ALL handler methods
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\handling\world\family\MapleFamily.java` (661 lines) — family domain
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\handling\world\family\MapleFamilyBuff.java` (152 lines) — buff catalog
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\handling\world\family\MapleFamilyCharacter.java` (347 lines) — member
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\tools\packet\FamilyPacket.java` (303 lines) — ALL packet encoding
- `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src\properties\send.properties` — send opcodes

## Existing code to reference for patterns
- `src/Maple.Core/Guilds/Guild.cs` — Guild domain model pattern
- `src/Maple.Application/Guilds/GuildService.cs` — service pattern (registry + business logic)
- `src/Maple.Application/Alliances/AllianceService.cs` — newly created alliance service pattern
- `src/Maple.Application/Alliances/InMemoryAllianceRepository.cs` — in-memory repo pattern
- `src/Maple.Adapters.V113/Channel/V113AllianceHandler.cs` — handler with session hook pattern
- `src/Maple.Adapters.V113/Channel/V113GuildPackets.cs` — packet encoding pattern
- `src/Maple.Core/IO/PacketWriter.cs` — PacketWriter API (WriteShort, WriteInt, WriteByte, WriteMapleString, WriteLong, etc.)

## Important notes
- Java `USE_FAMILY` has `!isGM()` check that blocks non-GMs. Our port should REMOVE this restriction — make the buff system work for everyone.
- Java `FAMILY_SUMMON` also has a GM check — remove it similarly.
- Do NOT modify `V113ChannelConnectionHandler.cs` — I will wire the dispatch myself
- Do NOT modify `V113ChannelOpcodes.cs` — already has all constants defined
- Create NEW files only
- Use `PacketWriter`/`PacketReader` from `Maple.Core.IO`
- xunit for tests
- No comments unless the WHY is non-obvious
- Character has FamilyId, SeniorId fields — check if they exist in `src/Maple.Core/Characters/Character.cs`, if not, add them
