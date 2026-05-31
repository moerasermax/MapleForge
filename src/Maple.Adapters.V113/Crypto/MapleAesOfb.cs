using System.Security.Cryptography;
using Maple.Versioning;

namespace Maple.Adapters.V113.Crypto;

/// <summary>
/// v113 AES-OFB cipher，移植自舊 Java <c>tools/MapleAESOFB.java</c>。
/// 有狀態：IV 隨每次 <see cref="Crypt"/> 演化。單方向、非執行緒安全（每連線每方向一個實例）。
/// </summary>
internal sealed class MapleAesOfb : IPacketCipher, IDisposable
{
    private const int BlockSize = 16;

    private readonly byte[] _iv = new byte[4];
    private readonly ushort _versionSwapped;
    private readonly Aes _aes;
    private readonly byte[] _ecbBuf = new byte[BlockSize];

    public MapleAesOfb(ReadOnlySpan<byte> iv, short version)
    {
        if (iv.Length != 4)
            throw new ArgumentException("IV 必須為 4 bytes", nameof(iv));
        iv.CopyTo(_iv);

        // 與 Java 建構子相同：把 version 的高低 byte 互換後保存。
        _versionSwapped = (ushort)(((version >> 8) & 0xFF) | ((version << 8) & 0xFF00));

        _aes = Aes.Create();
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
        _aes.Key = V113CryptoConstants.AesKey;
    }

    public void Crypt(Span<byte> data)
    {
        int remaining = data.Length;
        int llength = 0x5B0;
        int start = 0;

        Span<byte> myIv = stackalloc byte[BlockSize];

        while (remaining > 0)
        {
            // myIv = iv 重複 4 次（BitTools.multiplyBytes(iv, 4, 4)）。
            for (int i = 0; i < BlockSize; i++)
                myIv[i] = _iv[i % 4];

            if (remaining < llength)
                llength = remaining;

            for (int x = start; x < start + llength; x++)
            {
                if ((x - start) % BlockSize == 0)
                {
                    _aes.EncryptEcb(myIv, _ecbBuf, PaddingMode.None);
                    _ecbBuf.CopyTo(myIv);
                }
                data[x] ^= myIv[(x - start) % BlockSize];
            }

            start += llength;
            remaining -= llength;
            llength = 0x5B4;
        }

        NextIv();
    }

    public void WriteHeader(Span<byte> destFour, int length)
    {
        // getPacketHeader：用「當前 IV」，不推進。
        int iiv = ((_iv[3] & 0xFF) | ((_iv[2] << 8) & 0xFF00)) ^ _versionSwapped;
        int mlength = (((length << 8) & 0xFF00) | ((length >> 8) & 0xFF)) ^ iiv;

        destFour[0] = (byte)((iiv >> 8) & 0xFF);
        destFour[1] = (byte)(iiv & 0xFF);
        destFour[2] = (byte)((mlength >> 8) & 0xFF);
        destFour[3] = (byte)(mlength & 0xFF);
    }

    public int ReadLength(ReadOnlySpan<byte> header)
    {
        // getPacketLength（與 IV 無關）：高 16 ^ 低 16，再做 endian swap。
        int h = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
        int len = ((int)((uint)h >> 16)) ^ (h & 0xFFFF);
        len = ((len << 8) & 0xFF00) | ((len >> 8) & 0xFF);
        return len;
    }

    public bool Check(ReadOnlySpan<byte> header)
    {
        int versionHigh = (_versionSwapped >> 8) & 0xFF;
        int versionLow = _versionSwapped & 0xFF;
        return ((header[0] ^ _iv[2]) & 0xFF) == versionHigh
            && ((header[1] ^ _iv[3]) & 0xFF) == versionLow;
    }

    /// <summary>測試掛鉤：目前 IV 的副本（對照 Java getIv()）。</summary>
    internal byte[] CurrentIv => (byte[])_iv.Clone();

    /// <summary>updateIv → getNewIv：用 funnyShit 從舊 IV 推導新 IV。</summary>
    private void NextIv() => ComputeNextIv(_iv).CopyTo(_iv.AsSpan());

    /// <summary>getNewIv：純函式版（供黃金測試直接比對 Java 預言機）。</summary>
    internal static byte[] ComputeNextIv(ReadOnlySpan<byte> oldIv)
    {
        Span<byte> result = stackalloc byte[4] { 0xF2, 0x53, 0x50, 0xC6 }; // magic
        for (int i = 0; i < 4; i++)
            FunnyShit(oldIv[i], result);
        return result.ToArray();
    }

    /// <summary>移植自 MapleAESOFB.funnyShit（276–308 行）。嚴格保留 Java 的 byte 截斷語意。</summary>
    private static void FunnyShit(byte inputByte, Span<byte> inb)
    {
        byte[] fb = V113CryptoConstants.FunnyBytes;

        byte elina = inb[1];
        byte anna = inputByte;
        byte moritz = fb[elina & 0xFF];
        moritz = (byte)(moritz - inputByte);
        inb[0] = (byte)(inb[0] + moritz);
        moritz = inb[2];
        moritz = (byte)(moritz ^ fb[anna & 0xFF]);
        elina = (byte)(elina - (moritz & 0xFF));
        inb[1] = elina;
        elina = inb[3];
        moritz = elina;
        elina = (byte)(elina - (inb[0] & 0xFF));
        moritz = fb[moritz & 0xFF];
        moritz = (byte)(moritz + inputByte);
        moritz = (byte)(moritz ^ inb[2]);
        inb[2] = moritz;
        elina = (byte)(elina + (fb[anna & 0xFF] & 0xFF));
        inb[3] = elina;

        int merry = inb[0] & 0xFF;
        merry |= (inb[1] << 8) & 0xFF00;
        merry |= (inb[2] << 16) & 0xFF0000;
        merry |= unchecked((inb[3] << 24) & (int)0xFF000000);
        int retValue = (int)((uint)merry >> 0x1D);
        merry <<= 3;
        retValue |= merry;

        inb[0] = (byte)(retValue & 0xFF);
        inb[1] = (byte)((retValue >> 8) & 0xFF);
        inb[2] = (byte)((retValue >> 16) & 0xFF);
        inb[3] = (byte)((retValue >> 24) & 0xFF);
    }

    public void Dispose() => _aes.Dispose();
}
