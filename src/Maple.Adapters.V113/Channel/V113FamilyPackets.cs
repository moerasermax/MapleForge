using Maple.Application.Families;
using Maple.Core.Families;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal static class V113FamilyPackets
{
    public const short SendFamilyChartResult = 0x56;
    public const short SendFamilyInfoResult = 0x57;
    public const short SendFamilyResult = 0x58;
    public const short SendFamilyJoinRequest = 0x59;
    public const short SendFamilyJunior = 0x5A;
    public const short SendFamilyJoinAccepted = 0x5B;
    public const short SendFamilyPrivilegeList = 0x5C;
    public const short SendFamilyFamousPointIncResult = 0x5D;
    public const short SendFamilyNotifyLoginOrLogout = 0x5E;
    public const short SendFamilySetPrivilege = 0x5F;
    public const short SendFamilySummonRequest = 0x60;

    public static byte[] FamilyPrivilegeList()
    {
        var entries = FamilyBuff.All;
        var w = new PacketWriter();
        w.WriteShort(SendFamilyPrivilegeList);
        w.WriteInt(entries.Count);
        foreach (var entry in entries)
        {
            w.WriteByte(entry.Type);
            w.WriteInt(entry.RepCost);
            w.WriteInt(1);
            w.WriteMapleString(entry.BuffType);
            w.WriteMapleString($"{entry.BuffType}:{entry.Duration}");
        }

        return w.ToArray();
    }

    public static byte[] ChangeRep(int amount)
    {
        var w = new PacketWriter(10);
        w.WriteShort(SendFamilyFamousPointIncResult);
        w.WriteInt(amount);
        w.WriteInt(0);
        return w.ToArray();
    }

    public static byte[] FamilyInfo(FamilyInfoData info)
    {
        var w = new PacketWriter();
        w.WriteShort(SendFamilyInfoResult);
        w.WriteInt(info.CurrentRep);
        w.WriteInt(info.TotalRep);
        w.WriteInt(info.TotalRep);
        w.WriteShort(info.JuniorCount);
        w.WriteShort(2);
        w.WriteShort(info.JuniorCount);
        if (info.LeaderId > 0)
        {
            w.WriteInt(info.LeaderId);
            w.WriteMapleString(info.LeaderName);
            w.WriteMapleString(info.Notice);
        }
        else
        {
            w.WriteLong(0);
        }

        WriteUsedBuffs(w, info.UsedBuffs);
        return w.ToArray();
    }

    public static byte[] FamilyPedigree(FamilyPedigreeData pedigree)
    {
        var w = new PacketWriter();
        w.WriteShort(SendFamilyChartResult);
        w.WriteInt(pedigree.CharacterId);
        w.WriteInt(pedigree.Members.Count);
        foreach (var member in pedigree.Members)
        {
            AddFamilyCharInfo(w, member);
        }

        w.WriteLong(pedigree.DescendantSlots);
        w.WriteInt(pedigree.Generations);
        w.WriteInt(-1);
        w.WriteInt(pedigree.FamilyMemberCount);
        foreach (var descendant in pedigree.DescendantCounts)
        {
            w.WriteInt(descendant.CharacterId);
            w.WriteInt(descendant.DescendantCount);
        }

        WriteUsedBuffs(w, pedigree.UsedBuffs);
        w.WriteShort(2);
        return w.ToArray();
    }

    public static byte[] FamilyInvite(int inviterId, int inviterLevel, int inviterJob, string inviterName)
    {
        var w = new PacketWriter();
        w.WriteShort(SendFamilyJoinRequest);
        w.WriteInt(inviterId);
        w.WriteMapleString(inviterName);
        return w.ToArray();
    }

    public static byte[] FamilyResult(int type, int mesos = 0)
    {
        var w = new PacketWriter(10);
        w.WriteShort(SendFamilyResult);
        w.WriteInt(type);
        w.WriteInt(mesos);
        return w.ToArray();
    }

    public static byte[] FamilyJoinResponse(bool accepted, string addedName)
    {
        var w = new PacketWriter();
        w.WriteShort(SendFamilyJunior);
        w.WriteByte(accepted ? 1 : 0);
        w.WriteMapleString(addedName);
        return w.ToArray();
    }

    public static byte[] SeniorMessage(string seniorName)
    {
        var w = new PacketWriter();
        w.WriteShort(SendFamilyJoinAccepted);
        w.WriteMapleString(seniorName);
        return w.ToArray();
    }

    public static byte[] FamilySetPrivilege(FamilyBuffEntry entry)
    {
        var privilegeType = ToPrivilegeType(entry.Type);
        var w = new PacketWriter();
        w.WriteShort(SendFamilySetPrivilege);
        w.WriteByte(privilegeType);
        if (privilegeType >= 2 && privilegeType <= 4)
        {
            var amount = entry.Type is 2 or 3 ? 150 : 200;
            w.WriteInt(entry.Type);
            w.WriteInt(privilegeType == 3 ? 0 : amount);
            w.WriteInt(privilegeType == 2 ? 0 : amount);
            w.WriteByte(0);
            w.WriteInt(entry.Duration * 60_000);
        }

        return w.ToArray();
    }

    public static byte[] FamilyLoggedIn(bool online, string name)
    {
        var w = new PacketWriter();
        w.WriteShort(SendFamilyNotifyLoginOrLogout);
        w.WriteByte(online ? 1 : 0);
        w.WriteMapleString(name);
        return w.ToArray();
    }

    public static byte[] FamilySummonRequest(string summonerName, string mapName)
    {
        var w = new PacketWriter();
        w.WriteShort(SendFamilySummonRequest);
        w.WriteMapleString(summonerName);
        w.WriteMapleString(mapName);
        return w.ToArray();
    }

    private static void AddFamilyCharInfo(PacketWriter w, FamilyPedigreeMemberData member)
    {
        w.WriteInt(member.CharacterId);
        w.WriteInt(member.SeniorId);
        w.WriteShort(member.Job);
        w.WriteByte(member.Level);
        w.WriteByte(member.IsOnline ? 1 : 0);
        w.WriteInt(member.CurrentRep);
        w.WriteInt(member.TotalRep);
        w.WriteInt(member.TotalRep);
        w.WriteInt(member.TotalRep);
        w.WriteLong(Math.Max(member.Channel, 0));
        w.WriteMapleString(member.Name);
    }

    private static void WriteUsedBuffs(PacketWriter w, IReadOnlyList<FamilyBuffUsage> usedBuffs)
    {
        w.WriteInt(usedBuffs.Count);
        foreach (var used in usedBuffs)
        {
            w.WriteInt(used.BuffType);
            w.WriteInt(used.TimesUsed);
        }
    }

    private static byte ToPrivilegeType(int familyBuffType) => familyBuffType switch
    {
        2 or 5 or 7 or 9 => 2,
        3 or 6 or 8 or 10 => 3,
        4 => 4,
        _ => 0,
    };
}
