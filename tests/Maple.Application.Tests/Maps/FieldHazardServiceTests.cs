using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Tests.Maps;

/// <summary>P075：<see cref="FieldHazardService.ApplyHpDecay"/> 對照 Java <c>handleMap</c>/<c>handleCooldowns</c> 的 hurt 分支。</summary>
public sealed class FieldHazardServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
    private const int ProtectItem = 1072000;

    [Fact]
    public void ApplyHpDecay_AfterInterval_DamagesAliveUnprotectedPlayers()
    {
        var field = ColdField(mapId: 211040000);
        var plain = NewPlayer(1, hp: 100);
        var protectedPlayer = NewPlayer(2, hp: 100);
        protectedPlayer.Character.Equips.Add(new EquipEntry { Position = -7, ItemId = ProtectItem });
        var dead = NewPlayer(3, hp: 0);

        var damaged = new FieldHazardService().ApplyHpDecay(field, new[] { plain, protectedPlayer, dead }, Start.AddSeconds(11));

        Assert.Equal(new[] { plain }, damaged);
        Assert.Equal(80, plain.Hp);
        Assert.Equal(100, protectedPlayer.Hp);
        Assert.Equal(0, dead.Hp);
    }

    [Fact]
    public void ApplyHpDecay_BeforeInterval_DoesNothing()
    {
        var field = ColdField(mapId: 211040000);
        var player = NewPlayer(1, hp: 100);

        var damaged = new FieldHazardService().ApplyHpDecay(field, new[] { player }, Start.AddSeconds(10));

        Assert.Empty(damaged);
        Assert.Equal(100, player.Hp);
    }

    [Fact]
    public void ApplyHpDecay_NoPlayers_DoesNotAdvanceCycle()
    {
        // Java 只在 characterSize() > 0 才呼叫 canHurt()：空圖不推進 lastHurtTime。
        var field = ColdField(mapId: 211040000);
        var service = new FieldHazardService();

        service.ApplyHpDecay(field, Array.Empty<Player>(), Start.AddSeconds(11));
        var player = NewPlayer(1, hp: 100);
        var damaged = service.ApplyHpDecay(field, new[] { player }, Start.AddSeconds(12));

        Assert.Single(damaged);
        Assert.Equal(80, player.Hp);
    }

    [Fact]
    public void ApplyHpDecay_LethalDamage_ClampsToZero()
    {
        var field = ColdField(mapId: 211040000);
        var player = NewPlayer(1, hp: 5);

        new FieldHazardService().ApplyHpDecay(field, new[] { player }, Start.AddSeconds(11));

        Assert.Equal(0, player.Hp);
        Assert.False(player.IsAlive);
    }

    [Fact]
    public void ApplyHpDecay_MiniDungeon_CashProtectItemPreventsDamage()
    {
        var field = ColdField(mapId: FieldHpDecay.MiniDungeonMapId);
        var withTicket = NewPlayer(1, hp: 100);
        withTicket.GainItem(InventoryType.Cash, FieldHazardService.MiniDungeonProtectCashItemId);
        var without = NewPlayer(2, hp: 100);

        var damaged = new FieldHazardService().ApplyHpDecay(field, new[] { withTicket, without }, Start.AddSeconds(11));

        Assert.Equal(new[] { without }, damaged);
    }

    [Fact]
    public void ApplyHpDecay_NoDecayConfigured_DoesNothing()
    {
        var field = new FieldInstance(100000000);
        var player = NewPlayer(1, hp: 100);

        Assert.Empty(new FieldHazardService().ApplyHpDecay(field, new[] { player }, Start.AddHours(1)));
    }

    private static FieldInstance ColdField(int mapId)
    {
        var field = new FieldInstance(mapId);
        field.HpDecay = FieldHpDecay.Create(
            new MapData { MapId = mapId, DecHp = 20, DecHpInterval = 10_000, ProtectItem = ProtectItem },
            Start);
        return field;
    }

    private static Player NewPlayer(int id, short hp)
        => new(new Character { Id = id, Name = $"P{id}", Stats = new CharacterStats { Hp = hp, MaxHp = 100 } }, new Position(0, 0, 0, 0));
}
