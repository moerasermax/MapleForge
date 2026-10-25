using Maple.Application.Skills;
using Maple.Content.Wz;
using Maple.Core.Skills;

namespace Maple.Application.Tests.Skills;

public sealed class WzSkillCatalogTests : IDisposable
{
    private static readonly string WzDir = WzTestData.Directory;
    private readonly WzDataProvider _provider = new(WzDir);

    public void Dispose() => _provider.Dispose();

    [WzFact]
    public void GetSkill_LoadsMagicGuardFromSkillWz()
    {
        var catalog = new WzSkillCatalog(_provider);

        var skill = catalog.GetSkill(2001002);

        Assert.NotNull(skill);
        Assert.True(skill.MaxLevel > 0);
        var effect = skill.GetEffect(1);
        Assert.NotNull(effect);
        Assert.Contains(effect.Statups, static s => s.Stat == MapleBuffStat.MAGIC_GUARD);
    }
}
