using Maple.Core.Characters;

namespace Maple.Application.Characters;

/// <summary>
/// 角色相關業務邏輯（建角、查詢、名稱檢查）。
/// 規則對照舊 MapleCharacterUtil + CharLoginHandler。
/// </summary>
public sealed class CharacterService
{
    private readonly ICharacterRepository _repo;

    public CharacterService(ICharacterRepository repo) => _repo = repo;

    /// <summary>取得帳號下的角色清單。</summary>
    public Task<IReadOnlyList<Character>> GetCharactersAsync(int accountId, CancellationToken ct = default)
        => _repo.GetByAccountAsync(accountId, ct);

    /// <summary>依 ID 取角色；不存在回 null。</summary>
    public Task<Character?> GetByIdAsync(int characterId, CancellationToken ct = default)
        => _repo.FindByIdAsync(characterId, ct);

    /// <summary>更新角色文件。</summary>
    public Task UpdateAsync(Character character, CancellationToken ct = default)
        => _repo.UpdateAsync(character, ct);

    /// <summary>名稱是否可用（未被使用 + 格式合法）。</summary>
    public async Task<bool> IsNameAvailableAsync(string name, CancellationToken ct = default)
    {
        if (!IsValidName(name)) return false;
        return await _repo.FindByNameAsync(name, ct) is null;
    }

    /// <summary>
    /// 建立新角色（對照舊 handleCreateCharacter）。
    /// 若名稱已存在或格式不合法回 null；成功回已存入的角色（含 Id）。
    /// </summary>
    public async Task<Character?> CreateCharacterAsync(
        int accountId,
        byte gender,
        string name,
        int jobType,
        int face,
        int hair,
        List<EquipEntry> startEquips,
        CancellationToken ct = default)
    {
        if (!IsValidName(name)) return null;
        if (await _repo.FindByNameAsync(name, ct) is not null) return null;

        var (startMap, job) = jobType switch
        {
            0 => (130030000, (short)1000),  // 皇家騎士團
            2 => (914000000, (short)2000),  // 狂狼勇士
            _ => (0,         (short)0),     // 冒險家（default）
        };

        var chr = new Character
        {
            AccountId  = accountId,
            Name       = name,
            Gender     = gender,
            SkinColor  = jobType == 2 ? (byte)11 : jobType == 0 ? (byte)10 : (byte)0,
            Face       = face,
            Hair       = hair,
            Level      = 1,
            Job        = job,
            Stats      = new CharacterStats(),   // 預設 12/5/4/4/50/50/5/5
            MapId      = startMap,
            SpawnPoint = 0,
            Equips     = startEquips,
            Keymap     = CreateDefaultKeymap(),
        };

        await _repo.AddAsync(chr, ct);
        return chr;
    }

    // 名稱驗證規則（對照舊 MapleCharacterUtil.isEligibleCharName）
    private static bool IsValidName(string name)
    {
        if (name.Length is < 3 or > 15) return false;
        foreach (char c in name)
            if (!char.IsLetterOrDigit(c)) return false;
        return true;
    }

    private static List<KeyBindingRecord> CreateDefaultKeymap()
    {
        int[] keys = [2, 3, 4, 5, 6, 7, 16, 17, 18, 19, 23, 25, 26, 27, 29, 31, 34, 35, 37, 38, 40, 41, 43, 44, 45, 46, 48, 50, 56, 57, 59, 60, 61, 62, 63, 64, 65];
        int[] types = [4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 5, 4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 4, 4, 4, 5, 5, 6, 6, 6, 6, 6, 6, 6];
        int[] actions = [10, 12, 13, 18, 24, 21, 8, 5, 0, 4, 1, 19, 14, 15, 52, 2, 17, 11, 3, 20, 16, 23, 9, 50, 51, 6, 22, 7, 53, 54, 100, 101, 102, 103, 104, 105, 106];

        var keymap = new List<KeyBindingRecord>(keys.Length);
        for (var i = 0; i < keys.Length; i++)
        {
            keymap.Add(new KeyBindingRecord
            {
                Key = keys[i],
                Type = (byte)types[i],
                Action = actions[i],
            });
        }

        return keymap;
    }
}
