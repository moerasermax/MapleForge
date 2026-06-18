using Maple.Core.World;

namespace Maple.Core.Pets;

public readonly record struct PetGrowthResult(
    bool Success,
    bool FullnessIncreased,
    bool ClosenessChanged,
    bool LevelChanged);

public sealed class Pet
{
    private string _name;
    private byte _level;
    private short _closeness;
    private byte _fullness;

    public Pet(
        int petId,
        int itemId,
        string name,
        byte level,
        short closeness,
        byte fullness,
        int flags,
        Position position)
    {
        if (petId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(petId), petId, "Pet id must be positive.");
        }

        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), itemId, "Pet item id must be positive.");
        }

        PetId = petId;
        ItemId = itemId;
        _name = NormalizeName(name);
        Level = level;
        Closeness = closeness;
        Fullness = fullness;
        Flags = flags;
        Position = position;
    }

    public int PetId { get; }

    public int ItemId { get; }

    public string Name
    {
        get => _name;
        set => _name = NormalizeName(value);
    }

    public byte Level
    {
        get => _level;
        set => _level = (byte)System.Math.Clamp((int)value, 1, PetConstants.MaxLevel);
    }

    public short Closeness
    {
        get => _closeness;
        set => _closeness = (short)System.Math.Clamp((int)value, 0, PetConstants.MaxCloseness);
    }

    public byte Fullness
    {
        get => _fullness;
        set => _fullness = (byte)System.Math.Clamp((int)value, 0, PetConstants.MaxFullness);
    }

    public int Flags { get; set; }

    public Position Position { get; set; }

    public List<int> ExcludedItems { get; } = new();

    public PetGrowthResult Feed(bool gainCloseness)
    {
        var oldLevel = Level;
        var oldCloseness = Closeness;

        if (Fullness < PetConstants.MaxFullness)
        {
            Fullness = (byte)System.Math.Min(PetConstants.MaxFullness, Fullness + PetConstants.FeedFullnessGain);
            if (gainCloseness)
            {
                AddCloseness(1);
                RaiseLevelIfNeeded();
            }

            return new PetGrowthResult(
                Success: true,
                FullnessIncreased: true,
                ClosenessChanged: Closeness != oldCloseness,
                LevelChanged: Level != oldLevel);
        }

        if (gainCloseness)
        {
            Closeness = (short)System.Math.Max(0, Closeness - 1);
            LowerLevelIfNeeded();
        }

        return new PetGrowthResult(
            Success: true,
            FullnessIncreased: false,
            ClosenessChanged: Closeness != oldCloseness,
            LevelChanged: Level != oldLevel);
    }

    public PetGrowthResult FeedToFull(int closenessGain)
    {
        var oldLevel = Level;
        var oldCloseness = Closeness;
        var oldFullness = Fullness;

        Fullness = PetConstants.MaxFullness;
        AddCloseness(closenessGain);
        RaiseSingleLevelIfNeeded();

        return new PetGrowthResult(
            Success: true,
            FullnessIncreased: Fullness != oldFullness,
            ClosenessChanged: Closeness != oldCloseness,
            LevelChanged: Level != oldLevel);
    }

    public PetGrowthResult ExecuteCommand(int probability, int increase, int? roll = null)
    {
        var normalizedProbability = System.Math.Clamp(probability, 0, 100);
        var commandRoll = roll ?? Random.Shared.Next(99);
        if (commandRoll > normalizedProbability)
        {
            return new PetGrowthResult(false, false, false, false);
        }

        var oldLevel = Level;
        var oldCloseness = Closeness;
        AddCloseness(System.Math.Max(0, increase));
        RaiseLevelIfNeeded();

        return new PetGrowthResult(
            Success: true,
            FullnessIncreased: false,
            ClosenessChanged: Closeness != oldCloseness,
            LevelChanged: Level != oldLevel);
    }

    public void ReplaceExcludedItems(IEnumerable<int> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        ExcludedItems.Clear();
        foreach (var itemId in itemIds.Where(static id => id > 0).Take(10))
        {
            ExcludedItems.Add(itemId);
        }
    }

    private void AddCloseness(int amount)
    {
        if (amount <= 0 || Closeness >= PetConstants.MaxCloseness)
        {
            return;
        }

        Closeness = (short)System.Math.Min(PetConstants.MaxCloseness, Closeness + amount);
    }

    private void RaiseLevelIfNeeded()
    {
        while (Level < PetConstants.MaxLevel &&
               Closeness >= PetConstants.GetClosenessNeededForLevel(Level + 1))
        {
            Level++;
        }
    }

    private void RaiseSingleLevelIfNeeded()
    {
        if (Level < PetConstants.MaxLevel &&
            Closeness >= PetConstants.GetClosenessNeededForLevel(Level + 1))
        {
            Level++;
        }
    }

    private void LowerLevelIfNeeded()
    {
        while (Level > 1 &&
               Closeness < PetConstants.GetClosenessNeededForLevel(Level))
        {
            Level--;
        }
    }

    private static string NormalizeName(string? name)
        => string.IsNullOrWhiteSpace(name) ? "Pet" : name.Trim();
}
