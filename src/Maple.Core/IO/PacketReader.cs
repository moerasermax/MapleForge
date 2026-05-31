using System.Text;

namespace Maple.Core.IO;

/// <summary>
/// 純 little-endian 封包讀取器（對照舊 <c>LittleEndianAccessor</c>）。
/// 用於解析客戶端送來的封包 body（不含 4-byte 加密頭，opcode 為前 2 bytes）。
/// </summary>
public sealed class PacketReader
{
    private readonly byte[] _buf;
    private int _pos;

    public PacketReader(byte[] data, int offset = 0)
    {
        _buf = data;
        _pos = offset;
    }

    public int Remaining => _buf.Length - _pos;

    public byte ReadByte() => _buf[_pos++];

    public short ReadShort()
    {
        short v = (short)(_buf[_pos] | (_buf[_pos + 1] << 8));
        _pos += 2;
        return v;
    }

    public int ReadInt()
    {
        int v = _buf[_pos] | (_buf[_pos + 1] << 8) | (_buf[_pos + 2] << 16) | (_buf[_pos + 3] << 24);
        _pos += 4;
        return v;
    }

    /// <summary>MapleAsciiString：[short 長度][ASCII bytes]。</summary>
    public string ReadMapleString()
    {
        int len = ReadShort();
        if (len < 0 || len > Remaining)
            throw new InvalidDataException($"封包字串長度不合理：{len}");
        var s = Encoding.ASCII.GetString(_buf, _pos, len);
        _pos += len;
        return s;
    }

    public void Skip(int n) => _pos += n;
}
