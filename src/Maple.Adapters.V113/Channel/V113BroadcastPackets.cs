using Maple.Core.Inventory;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

/// <summary>v113 SERVERMESSAGE megaphone packet builders.</summary>
internal static class V113BroadcastPackets
{
    public const short SendServerMessage = V113ChannelSendOp.ServerMessage;
    public const short SendAvatarMega = V113ChannelSendOp.AvatarMega;

    /// <summary>對照 Java <c>MapleCharacter.dropMessage(1, msg)</c> / <c>getPopupMsg</c>：粉紅字彈出通知。</summary>
    public static byte[] PopupMessage(string message)
        => BroadcastMessage(type: 1, channel: 0, [message], ear: false, item: null);

    public static byte[] Megaphone(string message)
        => BroadcastMessage(type: 2, channel: 0, [message], ear: false, item: null);

    public static byte[] SuperMegaphone(string message, int channel, bool ear)
        => BroadcastMessage(type: 3, channel, [message], ear, item: null);

    public static byte[] ItemMegaphone(string message, int channel, bool ear, Item? item)
        => BroadcastMessage(type: 8, channel, [message], ear, item);

    public static byte[] TripleMegaphone(string[] messages, int channel, bool ear)
        => BroadcastMessage(type: 10, channel, messages, ear, item: null);

    public static byte[] HeartMegaphone(string message, int channel, bool ear)
        => BroadcastMessage(type: 11, channel, [message], ear, item: null);

    public static byte[] SkullMegaphone(string message, int channel, bool ear)
        => BroadcastMessage(type: 12, channel, [message], ear, item: null);

    /// <summary>
    /// 對照 Java <c>MaplePacketCreator.getGachaponMega</c>（<c>broadcastMessage</c> type=13）：
    /// 寶箱抽到稀有道具時的全服廣播。版型與 type=8（<see cref="ItemMegaphone"/>）不同：channel 是
    /// 4-byte int（非 1-byte）、沒有 ear 欄位、item 一定寫（無 null 判斷），故不走共用的
    /// <see cref="BroadcastMessage"/> 私有方法，獨立建包。
    /// </summary>
    public static byte[] GachaponMega(string message, int channel, Item item)
    {
        var w = new PacketWriter();
        w.WriteShort(SendServerMessage);
        w.WriteByte(13);
        w.WriteMapleString(message);
        w.WriteInt(channel - 1);
        AddItemInfo(w, item);
        return w.ToArray();
    }

    private static byte[] BroadcastMessage(byte type, int channel, IReadOnlyList<string>? messages, bool ear, Item? item)
    {
        var w = new PacketWriter();
        w.WriteShort(SendServerMessage);
        w.WriteByte(type);

        if (type == 4)
        {
            w.WriteByte(ear ? 1 : 0);
        }

        if (type != 4 || ear)
        {
            w.WriteMapleString(GetMessage(messages, 0));

            switch (type)
            {
                case 3:
                case 11:
                case 12:
                    WriteChannelAndEar(w, channel, ear);
                    break;

                case 8:
                    WriteChannelAndEar(w, channel, ear);
                    w.WriteByte(item is null ? 0 : 1);
                    if (item is not null)
                    {
                        AddItemInfo(w, item);
                    }
                    break;

                case 9:
                    WriteChannel(w, channel);
                    break;

                case 10:
                    WriteTripleMegaphoneBody(w, messages, channel, ear);
                    break;
            }
        }

        return w.ToArray();
    }

    private static void WriteTripleMegaphoneBody(
        PacketWriter w,
        IReadOnlyList<string>? messages,
        int channel,
        bool ear)
    {
        var lineCount = messages?.Count ?? 0;
        w.WriteByte(lineCount);
        if (lineCount > 1)
        {
            w.WriteMapleString(GetMessage(messages, 1));
        }

        if (lineCount > 2)
        {
            w.WriteMapleString(GetMessage(messages, 2));
        }

        WriteChannelAndEar(w, channel, ear);
    }

    private static string GetMessage(IReadOnlyList<string>? messages, int index)
        => messages is not null && index >= 0 && index < messages.Count
            ? messages[index]
            : string.Empty;

    private static void WriteChannelAndEar(PacketWriter w, int channel, bool ear)
    {
        WriteChannel(w, channel);
        w.WriteByte(ear ? 1 : 0);
    }

    private static void WriteChannel(PacketWriter w, int channel)
        => w.WriteByte(channel - 1);

    private static void AddItemInfo(PacketWriter w, Item item)
    {
        var hasUniqueId = item.UniqueId > 0 && item.ItemId / 10000 != 166;

        w.WriteByte(item.IsEquip ? 1 : 2);
        w.WriteInt(item.ItemId);
        w.WriteByte(hasUniqueId ? 1 : 0);
        if (hasUniqueId)
        {
            w.WriteLong(item.UniqueId);
        }

        w.WriteLong(GetTime(item.Expiration));

        if (item is Equip equip)
        {
            w.WriteByte(equip.UpgradeSlots);
            w.WriteByte(equip.Level);
            w.WriteShort(equip.Str);
            w.WriteShort(equip.Dex);
            w.WriteShort(equip.Int);
            w.WriteShort(equip.Luk);
            w.WriteShort(equip.Hp);
            w.WriteShort(equip.Mp);
            w.WriteShort(equip.Watk);
            w.WriteShort(equip.Matk);
            w.WriteShort(equip.Wdef);
            w.WriteShort(equip.Mdef);
            w.WriteShort(equip.Acc);
            w.WriteShort(equip.Avoid);
            w.WriteShort(equip.Hands);
            w.WriteShort(equip.Speed);
            w.WriteShort(equip.Jump);
            w.WriteMapleString(equip.Owner);
            w.WriteShort(equip.Flag);
            w.WriteByte(0);
            w.WriteByte(equip.ItemLevel);
            w.WriteInt(equip.ItemExp);
            if (!hasUniqueId)
            {
                w.WriteLong(equip.UniqueId);
            }

            w.WriteLong(GetTime(-2));
            w.WriteInt(-1);
        }
        else
        {
            w.WriteShort(item.Quantity);
            w.WriteMapleString(item.Owner);
            w.WriteShort(item.Flag);
        }
    }

    private static long GetTime(long offset)
    {
        const long KoreanEpochOffset = 116444736000000000L;
        if (offset < 0)
        {
            return KoreanEpochOffset + offset;
        }

        return KoreanEpochOffset + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 10000);
    }
}
