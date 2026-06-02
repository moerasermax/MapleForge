using Maple.Application.Npcs;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// 把版本無關的 <see cref="NpcDialog"/> 編成 v113 NPC_TALK(0x13C) 封包。
/// 對照 Java MaplePacketCreator.getNPCTalk / getNPCTalkText / getNPCTalkNum。
///
/// 統一布局：[short 0x13C][byte 4][int npcId][byte msgType][byte speakerType][maple-string text][trailing]。
/// （Java 對 getText/getNumber 用 writeShort(msgType) 且省略 type byte；當 speakerType=0 時
///  writeShort(n) 與 write(n)+write(0) 位元組完全等價，故此處統一。）
/// msgType：Ok/Next/Prev/NextPrev=0、YesNo=1、GetText=2、GetNumber=3、Simple=4。
/// trailing：msgType 0 = [prevFlag][nextFlag]；GetText = int 0 + int 0；GetNumber = def/min/max/0。
/// </summary>
internal static class V113NpcDialogEncoder
{
    public static byte[] Encode(NpcDialog dialog)
    {
        var (msgType, prev, next) = Classify(dialog.Kind);

        var w = new PacketWriter(dialog.Text.Length + 24);
        w.WriteShort(V113ChannelSendOp.NpcTalk);
        w.WriteByte(4);                       // NPC speaker 種類（4 = 一般 NPC 對話）
        w.WriteInt(dialog.NpcId);
        w.WriteByte((byte)msgType);
        w.WriteByte(dialog.SpeakerType);      // type：1=no ESC、3=show character… 預設 0
        w.WriteMapleString(dialog.Text);

        switch (dialog.Kind)
        {
            case NpcDialogKind.Ok:
            case NpcDialogKind.Next:
            case NpcDialogKind.Prev:
            case NpcDialogKind.NextPrev:
                w.WriteByte(prev);
                w.WriteByte(next);
                break;

            case NpcDialogKind.GetText:
                w.WriteInt(0);
                w.WriteInt(0);
                break;

            case NpcDialogKind.GetNumber:
                w.WriteInt(dialog.NumberDefault);
                w.WriteInt(dialog.NumberMin);
                w.WriteInt(dialog.NumberMax);
                w.WriteInt(0);
                break;

            // YesNo / Simple：無 trailing
        }

        return w.ToArray();
    }

    /// <summary>(msgType, prevButton, nextButton)。後二者僅 msgType 0 使用。</summary>
    private static (int msgType, byte prev, byte next) Classify(NpcDialogKind kind) => kind switch
    {
        NpcDialogKind.Ok => (0, 0, 0),
        NpcDialogKind.Next => (0, 0, 1),
        NpcDialogKind.Prev => (0, 1, 0),
        NpcDialogKind.NextPrev => (0, 1, 1),
        NpcDialogKind.YesNo => (1, 0, 0),
        NpcDialogKind.GetText => (2, 0, 0),
        NpcDialogKind.GetNumber => (3, 0, 0),
        NpcDialogKind.Simple => (4, 0, 0),
        _ => (0, 0, 0),
    };
}
