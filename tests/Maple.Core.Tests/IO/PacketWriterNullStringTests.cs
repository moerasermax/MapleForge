using Maple.Core.IO;

namespace Maple.Core.Tests.IO;

public sealed class PacketWriterNullStringTests
{
    [Fact]
    public void WriteMapleString_Null_WritesEmptyString()
    {
        var bytes = new PacketWriter().WriteMapleString(null).ToArray();

        Assert.Equal(new byte[] { 0, 0 }, bytes);
    }

    [Fact]
    public void WriteFixedAsciiString_Null_WritesZeroPadding()
    {
        var bytes = new PacketWriter().WriteFixedAsciiString(null, 4).ToArray();

        Assert.Equal(new byte[] { 0, 0, 0, 0 }, bytes);
    }
}
