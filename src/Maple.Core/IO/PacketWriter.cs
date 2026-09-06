namespace Maple.Core.IO;

/// <summary>
/// 純 little-endian 封包寫入器（對照舊 <c>MaplePacketLittleEndianWriter</c>）。
/// 無 I/O、無版本相依，可放在 Core。
/// </summary>
public sealed class PacketWriter
{
    private byte[] _buf;
    private int _len;

    public PacketWriter(int capacity = 32)
    {
        _buf = new byte[Math.Max(capacity, 16)];
        _len = 0;
    }

    private void Ensure(int extra)
    {
        if (_len + extra <= _buf.Length) return;
        int cap = _buf.Length * 2;
        while (cap < _len + extra) cap *= 2;
        Array.Resize(ref _buf, cap);
    }

    public PacketWriter WriteByte(int value)
    {
        Ensure(1);
        _buf[_len++] = (byte)value;
        return this;
    }

    public PacketWriter WriteShort(int value)
    {
        Ensure(2);
        _buf[_len++] = (byte)(value & 0xFF);
        _buf[_len++] = (byte)((value >> 8) & 0xFF);
        return this;
    }

    public PacketWriter WriteInt(int value)
    {
        Ensure(4);
        _buf[_len++] = (byte)(value & 0xFF);
        _buf[_len++] = (byte)((value >> 8) & 0xFF);
        _buf[_len++] = (byte)((value >> 16) & 0xFF);
        _buf[_len++] = (byte)((value >> 24) & 0xFF);
        return this;
    }

    public PacketWriter WriteLong(long value)
    {
        Ensure(8);
        for (int i = 0; i < 8; i++)
            _buf[_len++] = (byte)((value >> (i * 8)) & 0xFF);
        return this;
    }

    public PacketWriter WriteBytes(ReadOnlySpan<byte> bytes)
    {
        Ensure(bytes.Length);
        bytes.CopyTo(_buf.AsSpan(_len));
        _len += bytes.Length;
        return this;
    }

    /// <summary>MapleAsciiString：[short 長度（編碼後 byte 數）][<see cref="MapleTextEncoding"/> bytes]。</summary>
    public PacketWriter WriteMapleString(string s)
    {
        var bytes = MapleTextEncoding.Value.GetBytes(s);
        WriteShort(bytes.Length);
        WriteBytes(bytes);
        return this;
    }

    /// <summary>固定長度字串：寫入 len bytes，不足補 0（對照舊 writeAsciiString(name, 15)）。</summary>
    public PacketWriter WriteFixedAsciiString(string s, int len)
    {
        var bytes = MapleTextEncoding.Value.GetBytes(s);
        Ensure(len);
        var count = Math.Min(bytes.Length, len);
        int i = 0;
        for (; i < count; i++)
            _buf[_len++] = bytes[i];
        for (; i < len; i++)
            _buf[_len++] = 0;
        return this;
    }

    public PacketWriter WriteZeroBytes(int count)
    {
        Ensure(count);
        for (int i = 0; i < count; i++)
            _buf[_len++] = 0;
        return this;
    }

    public byte[] ToArray() => _buf.AsSpan(0, _len).ToArray();
}
