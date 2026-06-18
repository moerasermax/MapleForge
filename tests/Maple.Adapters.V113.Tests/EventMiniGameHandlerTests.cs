using Maple.Adapters.V113.Channel;
using Maple.Application.Events;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class EventMiniGameHandlerTests
{
    [Fact]
    public void BeansStart_DeductsBeanAndSendsShowAndUpdatePackets()
    {
        var handler = new V113EventMiniGameHandler(new CoconutEventService());
        var player = PlayerWithBeans(5);
        var request = new PacketWriter().WriteByte(0x04).ToArray();

        var result = handler.HandleBeansGameAction(new PacketReader(request), player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(4, player.Character.Beans);
        Assert.Equal(3, result.SelfPackets.Count);
        Assert.Equal(V113EventMiniGamePackets.SendBeanGameShow, Opcode(result.SelfPackets[0]));
        Assert.Equal(V113EventMiniGamePackets.SendUpdateBeans, Opcode(result.SelfPackets[1]));
    }

    [Fact]
    public void BeansShoot_DeductsShotCount()
    {
        var handler = new V113EventMiniGameHandler(new CoconutEventService());
        var player = PlayerWithBeans(10);
        handler.HandleBeansGameAction(new PacketReader(new PacketWriter().WriteByte(0x04).ToArray()), player);

        var request = new PacketWriter()
            .WriteByte(0x0E)
            .WriteByte(0)
            .WriteByte(3)
            .ToArray();

        var result = handler.HandleBeansGameAction(new PacketReader(request), player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(6, player.Character.Beans);
        Assert.Equal(V113EventMiniGamePackets.SendUpdateBeans, Opcode(result.SelfPackets[0]));
    }

    private static Player PlayerWithBeans(int beans)
        => new(
            new Character { Id = 1, Name = "Beans", Beans = beans },
            new Position(0, 0, 0, 0));

    private static short Opcode(byte[] packet) => new PacketReader(packet).ReadShort();
}
