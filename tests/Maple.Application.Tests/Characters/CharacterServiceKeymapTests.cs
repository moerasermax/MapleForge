using Maple.Application.Characters;
using Maple.Core.Characters;

namespace Maple.Application.Tests.Characters;

public sealed class CharacterServiceKeymapTests
{
    [Fact]
    public async Task CreateCharacter_SeedsJavaDefaultKeymap()
    {
        var repo = new FakeCharacterRepository();
        var service = new CharacterService(repo);

        var character = await service.CreateCharacterAsync(
            accountId: 1,
            gender: 0,
            name: "KeyHero",
            jobType: 1,
            face: 20000,
            hair: 30000,
            startEquips: [],
            ct: CancellationToken.None);

        Assert.NotNull(character);
        Assert.Equal(37, character.Keymap.Count);
        Assert.Contains(character.Keymap, k => k is { Key: 2, Type: 4, Action: 10 });
        Assert.Contains(character.Keymap, k => k is { Key: 29, Type: 5, Action: 52 });
        Assert.Contains(character.Keymap, k => k is { Key: 65, Type: 6, Action: 106 });
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

        public Task UpdateAsync(Character character, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
