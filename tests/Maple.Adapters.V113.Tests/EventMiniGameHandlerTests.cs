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

    [Fact]
    public void BeansMarqueeReward_AfterShoot_GrantsRewardAndBroadcastsMarquee()
    {
        var handler = new V113EventMiniGameHandler(new CoconutEventService());
        var player = PlayerWithBeans(10);
        handler.HandleBeansGameAction(new PacketReader(new PacketWriter().WriteByte(0x04).ToArray()), player); // start
        handler.HandleBeansGameAction(
            new PacketReader(new PacketWriter().WriteByte(0x0E).WriteByte(0).WriteByte(1).ToArray()), player); // shoot

        var result = handler.HandleBeansGameAction(new PacketReader(new PacketWriter().WriteByte(0x0D).ToArray()), player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(2008, player.Character.Beans);
        Assert.Equal(V113EventMiniGamePackets.SendBeanGameShoot, Opcode(result.SelfPackets[0])); // RewardBeans
        Assert.Equal(V113EventMiniGamePackets.SendUpdateBeans, Opcode(result.SelfPackets[1]));
        var marquee = Assert.Single(result.MapPackets);
        Assert.Equal(V113EventMiniGamePackets.SendBeansTips, Opcode(marquee));
    }

    [Fact]
    public void BeansMarqueeReward_WithoutPriorShoot_SendsOnlyEnableActions()
    {
        var handler = new V113EventMiniGameHandler(new CoconutEventService());
        var player = PlayerWithBeans(10);
        handler.HandleBeansGameAction(new PacketReader(new PacketWriter().WriteByte(0x04).ToArray()), player); // start

        var result = handler.HandleBeansGameAction(new PacketReader(new PacketWriter().WriteByte(0x0D).ToArray()), player);

        Assert.False(result.CharacterMutated);
        Assert.Equal(9, player.Character.Beans);
        Assert.Empty(result.MapPackets);
    }

    [Fact]
    public void BeansTiming_UnderFiveSeconds_Grants100AndUpdatesBeans()
    {
        var handler = new V113EventMiniGameHandler(new CoconutEventService());
        var player = PlayerWithBeans(10);
        handler.HandleBeansGameAction(new PacketReader(new PacketWriter().WriteByte(0x04).ToArray()), player); // start
        handler.HandleBeansGameAction(
            new PacketReader(new PacketWriter().WriteByte(0x0E).WriteByte(0).WriteByte(1).ToArray()), player); // shoot

        // 對照 Java：第一次 type=7 封包會把 beans_time 設成當下 now，故 elapsed 必為 0（<5s 分級）。
        var request = new PacketWriter().WriteByte(0x0F).WriteInt(0).WriteInt(1000).ToArray();
        var result = handler.HandleBeansGameAction(new PacketReader(request), player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(8 + 100, player.Character.Beans);
        Assert.Equal(V113EventMiniGamePackets.SendBeanGameShoot, Opcode(result.SelfPackets[0]));
        Assert.Equal(V113EventMiniGamePackets.SendUpdateBeans, Opcode(result.SelfPackets[1]));
    }

    [Fact]
    public void BeansTiming_OverTenSecondsSinceFirstCall_GrantsNothing_NoUpdateBeansPacket()
    {
        var handler = new V113EventMiniGameHandler(new CoconutEventService());
        var player = PlayerWithBeans(10);
        handler.HandleBeansGameAction(new PacketReader(new PacketWriter().WriteByte(0x04).ToArray()), player); // start
        handler.HandleBeansGameAction(
            new PacketReader(new PacketWriter().WriteByte(0x0E).WriteByte(0).WriteByte(1).ToArray()), player); // shoot
        // 第一次 type=7 建立起算時間（clientTime=0），依 Java 語意當次立刻算 elapsed=0 → 近分級(+100)。
        handler.HandleBeansGameAction(
            new PacketReader(new PacketWriter().WriteByte(0x0F).WriteInt(0).WriteInt(0).ToArray()), player);
        Assert.Equal(108, player.Character.Beans);

        var request = new PacketWriter().WriteByte(0x0F).WriteInt(0).WriteInt(10_001).ToArray();
        var result = handler.HandleBeansGameAction(new PacketReader(request), player);

        Assert.False(result.CharacterMutated);
        Assert.Equal(108, player.Character.Beans);
        Assert.Equal(2, result.SelfPackets.Count); // RewardBeans(0,5) + EnableActions，沒有 UpdateBeans
        Assert.DoesNotContain(result.SelfPackets, pkt => Opcode(pkt) == V113EventMiniGamePackets.SendUpdateBeans);
    }

    private static Player PlayerWithBeans(int beans)
        => new(
            new Character { Id = 1, Name = "Beans", Beans = beans },
            new Position(0, 0, 0, 0));

    private static short Opcode(byte[] packet) => new PacketReader(packet).ReadShort();
}
