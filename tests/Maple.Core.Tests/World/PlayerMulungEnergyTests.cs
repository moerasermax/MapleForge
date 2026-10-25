using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

/// <summary>P108：對照 Java <c>MapleCharacter.mulungEnergyModify</c>。</summary>
public sealed class PlayerMulungEnergyTests
{
    [Fact]
    public void ModifyMulungEnergy_Increase_AddsHundred()
    {
        var player = MakePlayer();

        Assert.Equal((short)100, player.ModifyMulungEnergy(increase: true));
        Assert.Equal((short)200, player.ModifyMulungEnergy(increase: true));
    }

    [Fact]
    public void ModifyMulungEnergy_Increase_CapsAtTenThousand()
    {
        var player = MakePlayer();
        for (var i = 0; i < 105; i++)
        {
            player.ModifyMulungEnergy(increase: true);
        }

        Assert.Equal(Player.MaxMulungEnergy, player.MulungEnergy);
    }

    [Fact]
    public void ModifyMulungEnergy_Decrease_ResetsToZero()
    {
        var player = MakePlayer();
        player.ModifyMulungEnergy(increase: true);

        Assert.Equal((short)0, player.ModifyMulungEnergy(increase: false));
    }

    private static Player MakePlayer()
        => new(new Character { Id = 1, Name = "Dojo" }, new Position(0, 0, 0, 0));
}
