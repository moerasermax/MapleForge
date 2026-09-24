using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Core.Tests.Skills;

/// <summary>P073：<see cref="Player.RemoveExpiredSkillCooldowns"/> 對照 Java <c>World.handleCooldowns</c>
/// 的 <c>startTime + length &lt; now</c> 判斷（嚴格小於，剛好到期那一刻仍算冷卻中）。</summary>
public sealed class PlayerSkillCooldownTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RemoveExpiredSkillCooldowns_RemovesOnlyStrictlyExpired()
    {
        var player = new Player(new Character { Id = 1, Name = "A" }, new Position(0, 0, 0, 0));
        player.AddSkillCooldown(100, Start, seconds: 10);
        player.AddSkillCooldown(200, Start, seconds: 20);

        Assert.Empty(player.RemoveExpiredSkillCooldowns(Start.AddSeconds(10))); // 剛好到期：Java 用 <，不移除
        Assert.Equal(new[] { 100 }, player.RemoveExpiredSkillCooldowns(Start.AddSeconds(11)));
        Assert.Empty(player.RemoveExpiredSkillCooldowns(Start.AddSeconds(12)));
        Assert.Equal(new[] { 200 }, player.RemoveExpiredSkillCooldowns(Start.AddSeconds(21)));
    }
}
