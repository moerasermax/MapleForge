namespace Maple.Core.Maps;

[Flags]
public enum FieldLimitType : long
{
    Jump = 0x1,
    MovementSkills = 0x2,
    SummoningBag = 0x04,
    MysticDoor = 0x08,
    ChannelSwitch = 0x10,
    RegularExpLoss = 0x20,
    VipRock = 0x40,
    Minigames = 0x80,
    NoClue1 = 0x100,
    Mount = 0x200,
    PotionUse = 0x400,
    Event = 0x2000,
    Pet = 0x8000,
    Event2 = 0x10000,
    DropDown = 0x20000,
}

public static class FieldLimitExtensions
{
    public static bool Check(this FieldLimitType flag, long fieldLimit)
        => ((long)flag & fieldLimit) != 0;
}
