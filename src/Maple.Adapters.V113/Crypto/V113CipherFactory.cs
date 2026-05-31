using Maple.Versioning;

namespace Maple.Adapters.V113.Crypto;

/// <summary>
/// v113 cipher 工廠。對照舊 <c>MapleServerHandler</c>：
/// 送出 cipher 用版本 <c>0xFFFF - 113</c>，接收 cipher 用版本 <c>113</c>。
/// </summary>
public sealed class V113CipherFactory : IVersionCipherFactory
{
    public (IPacketCipher Recv, IPacketCipher Send) CreateSessionPair(
        ReadOnlySpan<byte> recvIv, ReadOnlySpan<byte> sendIv)
    {
        var recv = new MapleAesOfb(recvIv, V113CryptoConstants.MapleVersion);
        var send = new MapleAesOfb(sendIv, unchecked((short)(0xFFFF - V113CryptoConstants.MapleVersion)));
        return (recv, send);
    }
}
