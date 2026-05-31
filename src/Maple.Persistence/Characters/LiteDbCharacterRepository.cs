using LiteDB;
using Maple.Core.Characters;

namespace Maple.Persistence.Characters;

/// <summary>
/// 以 LiteDB 實作的角色 repository。
/// collection "characters"；角色名稱唯一索引，accountId 索引加速角色列表查詢。
/// </summary>
public sealed class LiteDbCharacterRepository : ICharacterRepository
{
    private readonly ILiteCollection<Character> _col;

    public LiteDbCharacterRepository(LiteDatabase db)
    {
        _col = db.GetCollection<Character>("characters");
        _col.EnsureIndex(c => c.Name, unique: true);
        _col.EnsureIndex(c => c.AccountId);
    }

    public Task<IReadOnlyList<Character>> GetByAccountAsync(int accountId, CancellationToken ct = default)
    {
        var list = _col.Find(c => c.AccountId == accountId).ToList();
        return Task.FromResult<IReadOnlyList<Character>>(list);
    }

    public Task<Character?> FindByIdAsync(int characterId, CancellationToken ct = default)
    {
        var chr = _col.FindById(characterId);
        return Task.FromResult<Character?>(chr);
    }

    public Task<Character?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        var chr = _col.FindOne(c => c.Name == name);
        return Task.FromResult<Character?>(chr);
    }

    public Task AddAsync(Character character, CancellationToken ct = default)
    {
        _col.Insert(character);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Character character, CancellationToken ct = default)
    {
        _col.Update(character);
        return Task.CompletedTask;
    }
}
