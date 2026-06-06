using Maple.Content.Quests;

namespace Maple.Content.Tests.Quests;

public sealed class MinimalQuestCatalogTests
{
    [Fact]
    public void GetQuest_ReturnsRealJavaTemplateReferencedQuestIds()
    {
        var catalog = new MinimalQuestCatalog();

        Assert.NotNull(catalog.GetQuest(20000));
        Assert.NotNull(catalog.GetQuest(10370));
        Assert.Null(catalog.GetQuest(-1));
    }
}
