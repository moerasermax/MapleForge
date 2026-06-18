namespace Maple.Core.Families;

public sealed class FamilyMember
{
    public int CharacterId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SeniorId { get; set; }

    public int Junior1 { get; set; }

    public int Junior2 { get; set; }

    public int CurrentRep { get; set; }

    public int TotalRep { get; set; }

    public int Level { get; set; }

    public int Job { get; set; }

    public int JuniorCount => (Junior1 > 0 ? 1 : 0) + (Junior2 > 0 ? 1 : 0);

    public FamilyMember Clone() => new()
    {
        CharacterId = CharacterId,
        Name = Name,
        SeniorId = SeniorId,
        Junior1 = Junior1,
        Junior2 = Junior2,
        CurrentRep = CurrentRep,
        TotalRep = TotalRep,
        Level = Level,
        Job = Job,
    };

    public void SetSenior(int seniorId) => SeniorId = Math.Max(0, seniorId);

    public void SetJunior1(int juniorId) => Junior1 = Math.Max(0, juniorId);

    public void SetJunior2(int juniorId) => Junior2 = Math.Max(0, juniorId);

    public bool HasJunior(int juniorId) => juniorId > 0 && (Junior1 == juniorId || Junior2 == juniorId);

    public bool TryAddJunior(int juniorId)
    {
        if (juniorId <= 0 || HasJunior(juniorId))
        {
            return false;
        }

        if (Junior1 <= 0)
        {
            Junior1 = juniorId;
            return true;
        }

        if (Junior2 <= 0)
        {
            Junior2 = juniorId;
            return true;
        }

        return false;
    }

    public bool RemoveJunior(int juniorId)
    {
        if (Junior1 == juniorId)
        {
            Junior1 = Junior2;
            Junior2 = 0;
            return true;
        }

        if (Junior2 == juniorId)
        {
            Junior2 = 0;
            return true;
        }

        return false;
    }

    public bool TrySpendRep(int amount)
    {
        if (amount < 0 || CurrentRep < amount)
        {
            return false;
        }

        CurrentRep -= amount;
        return true;
    }

    public void GainRep(int amount)
    {
        var current = (long)CurrentRep + amount;
        var total = (long)TotalRep + Math.Max(0, amount);
        CurrentRep = current < 0 ? 0 : current > int.MaxValue ? int.MaxValue : (int)current;
        TotalRep = total > int.MaxValue ? int.MaxValue : (int)total;
    }

    public IReadOnlyList<FamilyMember> GetAllJuniors(Family family)
    {
        ArgumentNullException.ThrowIfNull(family);

        var result = new List<FamilyMember>();
        AddAllJuniors(family, this, result, new HashSet<int>());
        return result;
    }

    public IReadOnlyList<FamilyMember> GetOnlineJuniors(Family family, IReadOnlySet<int> onlineCharacterIds)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(onlineCharacterIds);

        var result = new List<FamilyMember> { this };
        AddOnlinePedigreeChild(family, Junior1, onlineCharacterIds, result);
        AddOnlinePedigreeChild(family, Junior2, onlineCharacterIds, result);
        return result;
    }

    private static void AddAllJuniors(Family family, FamilyMember member, List<FamilyMember> result, HashSet<int> visited)
    {
        if (!visited.Add(member.CharacterId))
        {
            return;
        }

        result.Add(member);
        AddAllJuniors(family, member.Junior1, result, visited);
        AddAllJuniors(family, member.Junior2, result, visited);
    }

    private static void AddAllJuniors(Family family, int characterId, List<FamilyMember> result, HashSet<int> visited)
    {
        var member = family.GetMember(characterId);
        if (member is not null)
        {
            AddAllJuniors(family, member, result, visited);
        }
    }

    private static void AddOnlinePedigreeChild(
        Family family,
        int characterId,
        IReadOnlySet<int> onlineCharacterIds,
        List<FamilyMember> result)
    {
        var member = family.GetMember(characterId);
        if (member is null)
        {
            return;
        }

        if (onlineCharacterIds.Contains(member.CharacterId))
        {
            result.Add(member);
        }

        AddOnlineGrandchild(family, member.Junior1, onlineCharacterIds, result);
        AddOnlineGrandchild(family, member.Junior2, onlineCharacterIds, result);
    }

    private static void AddOnlineGrandchild(
        Family family,
        int characterId,
        IReadOnlySet<int> onlineCharacterIds,
        List<FamilyMember> result)
    {
        var member = family.GetMember(characterId);
        if (member is not null && onlineCharacterIds.Contains(member.CharacterId))
        {
            result.Add(member);
        }
    }
}
