namespace Maple.Core.Characters;

/// <summary>角色持久層介面（由 Maple.Persistence 實作）。</summary>
public interface ICharacterRepository
{
    /// <summary>取得帳號下的所有角色。</summary>
    Task<IReadOnlyList<Character>> GetByAccountAsync(int accountId, CancellationToken ct = default);

    /// <summary>依 ID 取得單一角色（不存在回 null）。</summary>
    Task<Character?> FindByIdAsync(int characterId, CancellationToken ct = default);

    /// <summary>依名稱查詢（用於重名檢查）；不存在回 null。</summary>
    Task<Character?> FindByNameAsync(string name, CancellationToken ct = default);

    /// <summary>新增角色（LiteDB 自動分配 Id）。</summary>
    Task AddAsync(Character character, CancellationToken ct = default);

    /// <summary>更新角色文件。</summary>
    Task UpdateAsync(Character character, CancellationToken ct = default);
}
