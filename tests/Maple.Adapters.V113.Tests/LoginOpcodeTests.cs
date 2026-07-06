using Maple.Adapters.V113.Login;

namespace Maple.Adapters.V113.Tests;

public sealed class LoginOpcodeTests
{
    [Fact]
    public void ClientFeedbackAndClientErrorMatchJavaRecvPacketOpcode()
    {
        Assert.Equal(0x0C, V113RecvOp.ClientFeedback);
        Assert.Equal(0x0F, V113RecvOp.ClientError);
    }
}
