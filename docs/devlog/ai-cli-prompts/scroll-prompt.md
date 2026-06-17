## Task: Port Upgrade Scrolls — USE_UPGRADE_SCROLL (0x50)

MapleForge is a MapleStory v113 private server in C#/.NET 10. Solution at `MapleForge/MapleForge.slnx`. Working directory is `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\MapleForge`.

Java reference server at `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server`.

### Java Reference
- Opcode: `USE_UPGRADE_SCROLL = 0x50` (from recv.properties)
- Packet: `int tick (ignore), short scrollSlot, short equipSlot, short flags` (flags & 2 = white scroll)
- Java source: `InventoryHandler.UseUpgradeScroll` in `src/handling/channel/handler/InventoryHandler.java`
- Logic: get scroll from USE inv, get equip from EQUIPPED (|equipSlot| if <0) or EQUIP (if >=0), roll success, apply stats, handle fail/curse

### Core Mechanics
1. **Success**: apply scroll stat bonuses to equip, decrement UpgradeSlots, increment Level
2. **Fail**: decrement UpgradeSlots (unless white scroll protects)
3. **Curse** (scroll has cursed flag AND failed): equip DESTROYED
4. Always consume 1 scroll. If white scroll used (flags & 2), consume 1 item 2340000 from USE inv.

### What To Build

**1. Core: Scroll Data Model** — create `src/Maple.Core/Items/ScrollEffect.cs`:
```csharp
namespace Maple.Core.Items;
public sealed record ScrollEffect(int ScrollId, int SuccessRate, bool Cursed,
    short Str, short Dex, short Int, short Luk, short Hp, short Mp,
    short Watk, short Matk, short Wdef, short Mdef, short Acc, short Avoid,
    short Speed, short Jump);
public interface IScrollCatalog { ScrollEffect? GetScroll(int scrollId); }
```

**2. Application: ScrollService** — create `src/Maple.Application/Items/ScrollService.cs`:
```csharp
public enum ScrollResult { Success, Fail, Curse }
```
- Method: `ScrollUseResult UseScroll(Player player, short scrollSlot, short equipSlot, bool whiteScroll, int randomSeed)`
- Use `randomSeed % 100 < successRate` for deterministic testing
- On success: mutate equip stats (equip.Str += scroll.Str, etc.), equip.UpgradeSlots--, equip.Level++
- On fail + no white scroll: equip.UpgradeSlots--
- On fail + white scroll: slots unchanged, consume white scroll (2340000)
- On curse: remove equip from inventory
- Always consume scroll
- The `Equip` class (in `src/Maple.Core/Inventory/Item.cs`) already has all mutable stat fields

**3. Application: HardcodedScrollCatalog** — create `src/Maple.Application/Items/HardcodedScrollCatalog.cs`:
- 2040200 (Cape STR 100%): success=100, str=1
- 2040201 (Cape STR 60%): success=60, str=2
- 2040202 (Cape STR 10%): success=10, str=3, cursed=true
- 2044000 (Weapon ATK 100%): success=100, watk=1
- 2044001 (Weapon ATK 60%): success=60, watk=2
- 2044002 (Weapon ATK 10%): success=10, watk=3, cursed=true
- Catch-all for 204xxxx: success=100, watk=1

**4. Adapters: Scroll Packets + Handler** — create `src/Maple.Adapters.V113/Channel/V113ScrollHandler.cs`:
- Parse packet, call ScrollService, build packets
- ShowScrollEffect packet (find the send opcode — search Java `SendPacketOpcode.java` for `SHOW_SCROLL_EFFECT` or look for `getScrollEffect` in packet creator): `writeShort(opcode) + writeInt(charId) + writeByte(result: 1=success, 0=fail) + writeByte(legendarySpirit=0 for MVP) + writeByte(whiteScroll)`
- ModifyInventory packets for: scroll removal, equip update (or removal on curse)

**5. Wire up:**
- Add `UseUpgradeScroll = 0x50` to V113ChannelRecvOp
- Add ShowScrollEffect send opcode (search Java for the value)
- Add case in channel handler, register in DI

**6. Tests** — create `tests/Maple.Application.Tests/Items/ScrollServiceTests.cs`:
- 100% scroll success: stats increase, UpgradeSlots decreases
- Fail: UpgradeSlots decreases, stats unchanged
- White scroll on fail: UpgradeSlots unchanged
- Curse: equip destroyed
- Scroll consumed on all outcomes
- No upgrade slots → failure

### Existing Equip Model (src/Maple.Core/Inventory/Item.cs)
```csharp
public sealed class Equip : Item {
    public byte UpgradeSlots { get; set; }
    public byte Level { get; set; }
    public short Str/Dex/Int/Luk/Hp/Mp/Watk/Matk/Wdef/Mdef/Acc/Avoid/Speed/Jump { get; set; }
}
```

### Testing commands
```
dotnet test tests/Maple.Core.Tests/Maple.Core.Tests.csproj -v quiet --nologo
dotnet test tests/Maple.Application.Tests/Maple.Application.Tests.csproj -v quiet --nologo
dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo
```
Current: Core 65, App 118, Adapters 250+1skip.

### Architecture Rules
- Maple.Core must NOT import V113 types
- Follow existing handler patterns
- Use deterministic seed for scroll RNG (not Random.Shared) for testability
