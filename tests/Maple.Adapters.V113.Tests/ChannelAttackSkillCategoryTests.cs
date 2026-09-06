using Maple.Adapters.V113.Channel;

namespace Maple.Adapters.V113.Tests;

/// <summary>
/// P058：對照 Java <c>constants.SkillConstants.isCloseRangedAttack</c>/<c>isRangedAttack</c>/
/// <c>isMagicAttack</c>——三個攻擊封包（近戰/遠程/魔法）宣稱使用的技能 id 必須落在對應分類的
/// 硬編碼清單內，否則 Java 端會 registerOffense(ATTACK_TYPE_ERROR) 並整包丟棄。清單資料是機械
/// 抽取後逐行核對 Java switch-case 本體，這裡用「總數」+「已知邊界值」雙重把關，任何一個 id
/// 被漏轉都會讓對應測試失敗，降低手抄漏字誤傷合法玩家攻擊的風險。
/// </summary>
public sealed class ChannelAttackSkillCategoryTests
{
    // ── 總數比對（對照 Java switch-case 逐一數過的 case 數，任何一筆漏轉都會讓這裡先炸）──

    [Fact]
    public void CloseRangedAttackSkillIds_Count_Matches94()
    {
        Assert.Equal(94, V113ChannelConnectionHandler.CloseRangedAttackSkillIds.Count);
    }

    [Fact]
    public void RangedAttackSkillIds_Count_Matches58()
    {
        Assert.Equal(58, V113ChannelConnectionHandler.RangedAttackSkillIds.Count);
    }

    [Fact]
    public void MagicAttackSkillIds_Count_Matches35()
    {
        Assert.Equal(35, V113ChannelConnectionHandler.MagicAttackSkillIds.Count);
    }

    // ── isCloseRangedAttack（91 個）：起始/結尾/中段代表值 ─────────────────────

    [Theory]
    [InlineData(0)]      // 無技能的普通近戰攻擊
    [InlineData(1009)]
    [InlineData(1020)]
    [InlineData(1001004)]
    [InlineData(1311006)]
    [InlineData(21120010)] // 清單最後一筆
    public void IsCloseRangedAttackSkill_KnownIds_ReturnTrue(int skillId)
    {
        Assert.True(V113ChannelConnectionHandler.IsCloseRangedAttackSkill(skillId));
    }

    [Theory]
    [InlineData(3001004)]  // 屬於 isRangedAttack，不屬於近戰
    [InlineData(1000)]     // 屬於 isMagicAttack，不屬於近戰
    [InlineData(99999999)] // 不存在的技能 id
    public void IsCloseRangedAttackSkill_UnknownOrOtherCategoryIds_ReturnFalse(int skillId)
    {
        Assert.False(V113ChannelConnectionHandler.IsCloseRangedAttackSkill(skillId));
    }

    // ── isRangedAttack（58 個）：起始/結尾/中段代表值 ──────────────────────────

    [Theory]
    [InlineData(0)]        // 無技能的普通遠程攻擊
    [InlineData(3001004)]
    [InlineData(5220011)]
    [InlineData(21120006)] // 清單最後一筆
    public void IsRangedAttackSkill_KnownIds_ReturnTrue(int skillId)
    {
        Assert.True(V113ChannelConnectionHandler.IsRangedAttackSkill(skillId));
    }

    [Theory]
    [InlineData(1009)]     // 屬於 isCloseRangedAttack，不屬於遠程
    [InlineData(1000)]     // 屬於 isMagicAttack，不屬於遠程
    [InlineData(99999999)]
    public void IsRangedAttackSkill_UnknownOrOtherCategoryIds_ReturnFalse(int skillId)
    {
        Assert.False(V113ChannelConnectionHandler.IsRangedAttackSkill(skillId));
    }

    // ── isMagicAttack（35 個）：起始/結尾/中段代表值 ───────────────────────────

    [Theory]
    [InlineData(1000)]
    [InlineData(2121007)]
    [InlineData(20001000)] // 清單最後一筆
    public void IsMagicAttackSkill_KnownIds_ReturnTrue(int skillId)
    {
        Assert.True(V113ChannelConnectionHandler.IsMagicAttackSkill(skillId));
    }

    [Theory]
    [InlineData(0)]        // 魔法攻擊沒有「無技能」這回事，跟近戰/遠程不同
    [InlineData(1009)]     // 屬於 isCloseRangedAttack，不屬於魔法
    [InlineData(3001004)]  // 屬於 isRangedAttack，不屬於魔法
    [InlineData(99999999)]
    public void IsMagicAttackSkill_UnknownOrOtherCategoryIds_ReturnFalse(int skillId)
    {
        Assert.False(V113ChannelConnectionHandler.IsMagicAttackSkill(skillId));
    }
}
