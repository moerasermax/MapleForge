## Task: Port USE_ITEM (0x42) — Consumable Items (Potions/Food)

MapleForge is a MapleStory v113 private server in C#/.NET 10. Solution at `MapleForge/MapleForge.slnx`. Working directory is `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\MapleForge`.

Java reference server at `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server`.

### Java Reference
- Opcode: `USE_ITEM = 0x42` (from `src/properties/recv.properties`)
- Packet: `int tick (ignore), short slot, int itemId`
- Logic: validate item at slot, get item effect (hp/mp/hpR/mpR recovery), apply to player, remove 1 item, send UpdateStats
- Java source: `InventoryHandler.UseItem` in `src/handling/channel/handler/InventoryHandler.java`

### What To Build

**1. Core: Item Effect Data Model** — create `src/Maple.Core/Items/ItemEffect.cs`:
```csharp
namespace Maple.Core.Items;
public sealed record ItemEffect(int ItemId, int Hp, int Mp, int HpRate, int MpRate);
public interface IItemEffectCatalog { ItemEffect? GetEffect(int itemId); }
```

**2. Application: UseItemService** — create `src/Maple.Application/Items/UseItemService.cs`:
- Method: `UseItemResult Use(Player player, short slot, int itemId)`
- Validate: player alive (Hp > 0), item at slot with matching itemId, qty >= 1
- Get effect from IItemEffectCatalog; if null → fail
- Calculate HP: `effect.Hp + (character.MaxHp * effect.HpRate / 100)`, clamp to MaxHp. Same for MP.
- Apply to `player.Character.Hp` / `player.Character.Mp`
- Consume via `player.TryTakeItemFromSlot(InventoryType.Use, slot, itemId, 1, out mutation)`
- Return result with stat changes + inventory mutation + success flag

**3. Application: HardcodedItemEffectCatalog** — create `src/Maple.Application/Items/HardcodedItemEffectCatalog.cs`:
Common v113 potions:
- 2000000 (Red Potion): hp=50
- 2000001 (Orange Potion): hp=150
- 2000002 (White Potion): hp=300
- 2000003 (Blue Potion): mp=100
- 2000006 (Mana Elixir): mp=300
- 2001000 (Elixir): hpRate=50, mpRate=50
- 2001001 (Power Elixir): hpRate=100, mpRate=100
- Catch-all for range 2000000-2099999: hp=100 (unknown potions still work minimally)

**4. Adapters: Handler** — create `src/Maple.Adapters.V113/Channel/V113UseConsumableHandler.cs`:
(Note: V113ItemUseHandler already exists for a different opcode set - summon bag/mount food/catch/return scroll. Create a NEW file.)
- Parse: skip 4 bytes (tick), readShort (slot), readInt (itemId)
- Call UseItemService.Use()
- Build packets: ModifyInventoryQuantity (consume) + UpdateStats (hp/mp) + EnableActions
- Follow same result-struct pattern as `V113UseCashItemHandler`

**5. Wire up:**
- Add `UseItem = 0x42` to `V113ChannelRecvOp` in `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- Add case in `V113ChannelConnectionHandler.cs` main switch
- Register services in DI at `src/Maple.Host.Shared/MapleServerHost.cs`:
  - `builder.Services.AddSingleton<IItemEffectCatalog, HardcodedItemEffectCatalog>();`
  - `builder.Services.AddSingleton<UseItemService>();`
  - `builder.Services.AddSingleton<V113UseConsumableHandler>();`

**6. Tests** — create `tests/Maple.Application.Tests/Items/UseItemServiceTests.cs`:
- Potion heals HP correctly
- Rate-based healing (50% MaxHp)
- HP clamped to MaxHp
- Item consumed after use
- Dead player (Hp=0) cannot use
- Unknown item returns failure
- Missing item returns failure

### Existing Code To Reuse
- `Player.TryTakeItemFromSlot(type, slot, itemId, qty, out mutation)` — consumption
- `Player.FlushInventory()` — after mutation
- `V113StatsPackets.UpdateStats(...)` and `EnableActions()` — packets
- `V113ShopPackets.ModifyInventoryQuantity(mutation)` — inventory change
- `InventoryType.Use` — inventory tab
- Player.Character has: Hp, Mp, MaxHp, MaxMp (all int, settable)

### Testing commands
```
dotnet test tests/Maple.Core.Tests/Maple.Core.Tests.csproj -v quiet --nologo
dotnet test tests/Maple.Application.Tests/Maple.Application.Tests.csproj -v quiet --nologo
dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo
```
Current: Core 65, App 118, Adapters 250+1skip. All must pass, new test count should be higher.

### Architecture Rules
- `Maple.Core` must NOT import V113 or Adapters types
- Follow existing handler pattern (see V113UseCashItemHandler for reference)
- No comments unless the "why" is non-obvious
