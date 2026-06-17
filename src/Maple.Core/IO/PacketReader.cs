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

    public byte ReadByte()
    {
        if (_pos >= _buf.Length)
            throw new InvalidDataException($"封包不足：需 1 byte，剩餘 {Remaining}");
        return _buf[_pos++];
    }

    public short ReadShort()
    {
        if (Remaining < 2)
            throw new InvalidDataException($"封包不足：需 2 bytes，剩餘 {Remaining}");
        short v = (short)(_buf[_pos] | (_buf[_pos + 1] << 8));
        _pos += 2;
        return v;
    }

    public int ReadInt()
    {
        if (Remaining < 4)
            throw new InvalidDataException($"封包不足：需 4 bytes，剩餘 {Remaining}");
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

    /// <summary>讀取指定數量的位元組。</summary>
    public byte[] ReadBytes(int count)
    {
        if (count < 0 || count > Remaining)
            throw new InvalidDataException($"封包不足：需 {count} bytes，剩餘 {Remaining}");
        var result = new byte[count];
        Array.Copy(_buf, _pos, result, 0, count);
        _pos += count;
        return result;
    }
}
