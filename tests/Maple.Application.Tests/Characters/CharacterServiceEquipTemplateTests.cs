using Maple.Application.Characters;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.Items;

namespace Maple.Application.Tests.Characters;

/// <summary>P102：對照 Java CharLoginHandler 的 ii.getEquipById，建角初始裝備帶資料檔預設數值。</summary>
public sealed class CharacterServiceEquipTemplateTests
{
    [Fact]
    public async Task CreateCharacter_StartEquips_GetTemplateStatsAndKeepPosition()
    {
        var service = new CharacterService(new FakeCharacterRepository(), new FakeTemplates());
        var equips = new List<EquipEntry>
        {
            new() { Position = -11, ItemId = 1312004 },
            new() { Position = -5, ItemId = 1040002 },
        };

        var character = await service.CreateCharacterAsync(1, 0, "EquipHero", 1, 20000, 30000, equips);

        Assert.NotNull(character);
        var weapon = Assert.Single(character.Equips, e => e.ItemId == 1312004);
        Assert.Equal((short)-11, weapon.Position);
        Assert.Equal((short)17, weapon.Watk);
        Assert.Equal((byte)7, weapon.UpgradeSlots);
        var top = Assert.Single(character.Equips, e => e.ItemId == 1040002);
        Assert.Equal((short)-5, top.Position);
        Assert.Equal((short)17, top.Watk);
    }

    [Fact]
    public async Task CreateCharacter_WithoutTemplates_LeavesEquipStatsUntouched()
    {
        var service = new CharacterService(new FakeCharacterRepository());
        var equips = new List<EquipEntry> { new() { Position = -11, ItemId = 1312004 } };

        var character = await service.CreateCharacterAsync(1, 0, "BlankHero", 1, 20000, 30000, equips);

        var weapon = Assert.Single(character!.Equips);
        Assert.Equal((short)0, weapon.Watk);
    }

    private sealed class FakeTemplates : IEquipTemplateProvider
    {
        public Equip Create(int itemId)
            => new() { ItemId = itemId, Quantity = 1, Watk = 17, UpgradeSlots = 7 };
    }

    private sealed class FakeCharacterRepository : ICharacterRepository
    {
        private readonly List<Character> _characters = new();
        private int _nextId = 1;

        public Task<IReadOnlyList<Character>> GetByAccountAsync(int accountId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Character>>(_characters.Where(c => c.AccountId == accountId).ToArray());

        public Task<Character?> FindByIdAsync(int characterId, CancellationToken ct = default)
            => Task.FromResult(_characters.FirstOrDefault(c => c.Id == characterId));

        public Task<Character?> FindByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_characters.FirstOrDefault(c => c.Name == name));

        public Task AddAsync(Character character, CancellationToken ct = default)
        {
            character.Id = _nextId++;
            _characters.Add(character);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Character character, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> DeleteAsync(int characterId, CancellationToken ct = default) => Task.FromResult(false);
    }
}
