using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

/// <summary>P191：Java MapleCharacter.getBossLog（過去 24 小時筆數）／setBossLog／getOneTimeLog／setOneTimeLog。</summary>
public sealed class PlayerBossLogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BossLog_CountsLast24Hours_PerBoss()
    {
        var player = new Player(new Character { Id = 1, Name = "B" }, new Position(0, 0, 0, 0));

        player.SetBossLog("ZAK", T0);
        player.SetBossLog("ZAK", T0.AddHours(2));
        player.SetBossLog("HT", T0.AddHours(3));

        Assert.Equal(2, player.GetBossLog("ZAK", T0.AddHours(3)));
        Assert.Equal(1, player.GetBossLog("ZAK", T0.AddHours(25))); // 第一筆已超過 24 小時
        Assert.Equal(0, player.GetBossLog("ZAK", T0.AddHours(27)));
        Assert.Equal(1, player.GetBossLog("HT", T0.AddHours(26)));

        player.SetBossLog("PB", T0.AddDays(3)); // 寫入時清掉過期紀錄
        Assert.Single(player.Character.BossLog);
    }

    [Fact]
    public void OneTimeLog_Accumulates()
    {
        var player = new Player(new Character { Id = 1, Name = "B" }, new Position(0, 0, 0, 0));

        Assert.Equal(0, player.GetOneTimeLog("fk1"));
        player.SetOneTimeLog("fk1");
        player.SetOneTimeLog("fk1");

        Assert.Equal(2, player.GetOneTimeLog("fk1"));
    }
}
