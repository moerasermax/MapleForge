using Maple.Application.Quests;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Quests;
using Maple.Core.Shops;

namespace Maple.Adapters.V113.Channel;

/// <summary>v113 quest parser/packets. Layout mirrors OdinMS NPCHandler.QuestAction and MaplePacketCreator quest methods.</summary>
internal static class V113QuestPackets
{
    public const short RecvQuestAction = 0x65;
    public const short RecvUpdateQuest = 0x10B;
    public const short RecvUseItemQuest = 0x10D;

    public const short SendShowStatusInfo = 0x25;
    public const short SendShowQuestCompletion = 0x2E;
    public const short SendUpdateQuestInfo = 0xCC;

    public static QuestClientAction ParseQuestAction(PacketReader reader)
    {
        var kind = (QuestClientActionKind)reader.ReadByte();
        var questId = reader.ReadShort() & 0xFFFF;

        return kind switch
        {
            QuestClientActionKind.RestoreLostItem => ParseRestoreLostItem(reader, questId),
            QuestClientActionKind.Start => new QuestClientAction(kind, questId, NpcId: reader.ReadInt()),
            QuestClientActionKind.Complete => ParseComplete(reader, questId),
            QuestClientActionKind.Forfeit => new QuestClientAction(kind, questId),
            QuestClientActionKind.ScriptedStart => ParseScriptedStart(reader, questId, kind),
            QuestClientActionKind.ScriptedComplete => new QuestClientAction(kind, questId, NpcId: reader.ReadInt()),
            _ => new QuestClientAction(kind, questId),
        };
    }

    public static int ParseUpdateQuest(PacketReader reader) => reader.ReadShort() & 0xFFFF;

    public static byte[] UpdateQuest(QuestRecord quest)
    {
        var w = new PacketWriter();
        w.WriteShort(SendShowStatusInfo);
        w.WriteByte(1);
        w.WriteShort(quest.QuestId);
        w.WriteByte(quest.Status);

        switch ((QuestStatus)quest.Status)
        {
            case QuestStatus.NotStarted:
                w.WriteZeroBytes(10);
                break;
            case QuestStatus.Started:
                w.WriteMapleString(quest.CustomData ?? string.Empty);
                break;
            case QuestStatus.Completed:
                w.WriteLong(GetFileTime(quest.CompletionTimeUnixMillis));
                break;
            default:
                w.WriteZeroBytes(10);
                break;
        }

        return w.ToArray();
    }

    public static byte[] UpdateQuestMobKills(QuestRecord quest)
    {
        var text = string.Concat(quest.MobKills.Select(k => k.Count.ToString("D3")));
        var w = new PacketWriter();
        w.WriteShort(SendShowStatusInfo);
        w.WriteByte(1);
        w.WriteShort(quest.QuestId);
        w.WriteByte((byte)QuestStatus.Started);
        w.WriteMapleString(text);
        w.WriteZeroBytes(8);
        return w.ToArray();
    }

    public static byte[] UpdateInfoQuest(int questId, string data)
    {
        var w = new PacketWriter();
        w.WriteShort(SendShowStatusInfo);
        w.WriteByte(0x0A);
        w.WriteShort(questId);
        w.WriteMapleString(data);
        return w.ToArray();
    }

    public static byte[] UpdateQuestInfo(int questId, int npc, byte progress = 8, int nextQuest = 0)
    {
        var w = new PacketWriter();
        w.WriteShort(SendUpdateQuestInfo);
        w.WriteByte(progress);
        w.WriteShort(questId);
        w.WriteInt(npc);
        w.WriteInt(nextQuest);
        return w.ToArray();
    }

    public static byte[] UpdateQuestFinish(int questId, int npc, int nextQuest)
        => UpdateQuestInfo(questId, npc, progress: 8, nextQuest);

    public static byte[] ShowQuestCompletion(int questId)
    {
        var w = new PacketWriter(4);
        w.WriteShort(SendShowQuestCompletion);
        w.WriteShort(questId);
        return w.ToArray();
    }

    public static byte[] ModifyInventoryAdd(InventoryType type, Item item)
        => V113ShopPackets.ModifyInventoryAdd(type, item);

    public static byte[] ModifyInventoryQuantity(QuestInventoryMutation mutation)
        => V113ShopPackets.ModifyInventoryQuantity(new ShopInventoryMutation(
            mutation.Type,
            mutation.Slot,
            mutation.ItemId,
            mutation.OldQuantity,
            mutation.NewQuantity));

    public static byte[] UpdateMeso(int meso) => V113ShopPackets.UpdateMeso(meso);

    private static QuestClientAction ParseRestoreLostItem(PacketReader reader, int questId)
    {
        if (reader.Remaining >= 4)
        {
            reader.ReadInt();
        }

        var itemId = reader.Remaining >= 4 ? reader.ReadInt() : 0;
        return new QuestClientAction(QuestClientActionKind.RestoreLostItem, questId, RestoreItemId: itemId);
    }

    private static QuestClientAction ParseComplete(PacketReader reader, int questId)
    {
        var npc = reader.ReadInt();
        if (reader.Remaining >= 4)
        {
            reader.ReadInt();
        }

        var selection = reader.Remaining >= 4 ? reader.ReadInt() : (int?)null;
        return new QuestClientAction(QuestClientActionKind.Complete, questId, NpcId: npc, Selection: selection);
    }

    private static QuestClientAction ParseScriptedStart(PacketReader reader, int questId, QuestClientActionKind kind)
    {
        var npc = reader.ReadInt();
        if (reader.Remaining >= 4)
        {
            reader.ReadInt();
        }

        return new QuestClientAction(kind, questId, NpcId: npc);
    }

    private static long GetFileTime(long unixMillis)
    {
        const long fileTimeOffset = 116444736000000000L;
        var millis = unixMillis > 0 ? unixMillis : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return fileTimeOffset + (millis * 10000);
    }
}
