using Maple.Adapters.V113.Channel;
using Maple.Application.Quests;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.Quests;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelQuestPacketTests
{
    [Fact]
    public void ParseQuestAction_Complete_ReadsNpcTickAndSelection()
    {
        var w = new PacketWriter();
        w.WriteByte((byte)QuestClientActionKind.Complete);
        w.WriteShort(1000);
        w.WriteInt(1012000);
        w.WriteInt(123456);
        w.WriteInt(2);

        var action = V113QuestPackets.ParseQuestAction(new PacketReader(w.ToArray()));

        Assert.Equal(QuestClientActionKind.Complete, action.Kind);
        Assert.Equal(1000, action.QuestId);
        Assert.Equal(1012000, action.NpcId);
        Assert.Equal(2, action.Selection);
    }

    [Fact]
    public void UpdateQuest_Started_WritesShowStatusInfoQuestString()
    {
        var quest = new QuestRecord
        {
            QuestId = 1000,
            Status = (byte)QuestStatus.Started,
            CustomData = "007",
        };

        var r = new PacketReader(V113QuestPackets.UpdateQuest(quest));

        Assert.Equal(V113QuestPackets.SendShowStatusInfo, r.ReadShort());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(1000, r.ReadShort());
        Assert.Equal((byte)QuestStatus.Started, r.ReadByte());
        Assert.Equal("007", r.ReadMapleString());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void UpdateQuest_Completed_WritesJavaFileTime()
    {
        var quest = new QuestRecord
        {
            QuestId = 1000,
            Status = (byte)QuestStatus.Completed,
            CompletionTimeUnixMillis = 0,
        };

        var r = new PacketReader(V113QuestPackets.UpdateQuest(quest));

        Assert.Equal(V113QuestPackets.SendShowStatusInfo, r.ReadShort());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(1000, r.ReadShort());
        Assert.Equal((byte)QuestStatus.Completed, r.ReadByte());
        Assert.True(r.ReadLongForTest() > 116444736000000000L);
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void ShowQuestCompletion_WritesOpcodeAndQuestId()
    {
        byte[] expected =
        {
            0x2E, 0x00,
            0xE8, 0x03,
        };

        Assert.Equal(expected, V113QuestPackets.ShowQuestCompletion(1000));
    }

    [Fact]
    public void SetField_WritesBuddyQuestAndQuestInfoPacketInJavaCharacterInfoOrder()
    {
        var character = new Character
        {
            Id = 7,
            Name = "QuestChar",
            MapId = 100000000,
            BuddyList = new BuddyList { Capacity = 25 },
            Quests =
            [
                new QuestRecord
                {
                    QuestId = 20000,
                    Status = (byte)QuestStatus.Started,
                    CustomData = "001",
                },
                new QuestRecord
                {
                    QuestId = 20010,
                    Status = (byte)QuestStatus.Completed,
                    CompletionTimeUnixMillis = 1_700_000_000_000,
                },
            ],
            QuestInfo =
            [
                new QuestInfoRecord { QuestId = 20015, Data = "info" },
            ],
        };

        var reader = new PacketReader(V113ChannelPackets.SetField(character, channelIndex: 0));

        Assert.Equal(V113ChannelSendOp.SetField, reader.ReadShort());
        reader.Skip(4 + 1 + 1 + 2 + 12); // channel/login flags/CRand
        reader.Skip(8 + 1);              // addCharacterInfo marker
        reader.Skip(129);                // addCharStats

        Assert.Equal(25, reader.ReadByte());
        Assert.Equal(0, reader.ReadByte()); // no Bless of Fairy
        reader.Skip(8);                     // login time
        reader.Skip(36);                    // empty inventory section

        Assert.Equal(0, reader.ReadShort()); // skills
        Assert.Equal(0, reader.ReadShort()); // cooldowns

        Assert.Equal(1, reader.ReadShort()); // started quests
        Assert.Equal(20000, reader.ReadShort());
        Assert.Equal("001", reader.ReadMapleString());

        Assert.Equal(1, reader.ReadShort()); // completed quests
        Assert.Equal(20010, reader.ReadShort());
        var completedStart = reader.ReadInt();
        var completedEnd = reader.ReadInt();
        Assert.Equal(completedStart, completedEnd);

        reader.Skip(8);  // addRingInfo empty stubs
        reader.Skip(60); // addRocksInfo empty stubs
        Assert.Equal(0, reader.ReadInt());  // monster book cover
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal(0, reader.ReadShort());

        Assert.Equal(1, reader.ReadShort()); // QuestInfoPacket
        Assert.Equal(20015, reader.ReadShort());
        Assert.Equal("info", reader.ReadMapleString());

        Assert.Equal(0, reader.ReadShort());
        Assert.Equal(0, reader.ReadShort());
        Assert.Equal(0, reader.ReadShort());
        reader.Skip(8); // trailing server time
        Assert.Equal(0, reader.Remaining);
    }
}

internal static class PacketReaderQuestTestExtensions
{
    public static long ReadLongForTest(this PacketReader reader)
    {
        var low = (uint)reader.ReadInt();
        var high = (uint)reader.ReadInt();
        return (long)(((ulong)high << 32) | low);
    }
}
