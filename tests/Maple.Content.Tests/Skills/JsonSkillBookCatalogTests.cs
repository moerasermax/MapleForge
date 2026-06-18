using System.Text;
using Maple.Content.Skills;

namespace Maple.Content.Tests.Skills;

public sealed class JsonSkillBookCatalogTests
{
    [Fact]
    public void GetByItemId_LoadsSkillBookDefinitionFromJson()
    {
        const string json = """
        {
          "items": [
            {
              "itemId": 2290000,
              "skillIds": [1121000, 1221000],
              "successRate": 70,
              "reqSkillLevel": 5,
              "masterLevel": 20
            }
          ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var catalog = new JsonSkillBookCatalog(stream);

        var book = catalog.GetByItemId(2290000);

        Assert.NotNull(book);
        Assert.Equal(new[] { 1121000, 1221000 }, book!.SkillIds);
        Assert.Equal(70, book.SuccessRate);
        Assert.Equal(5, book.ReqSkillLevel);
        Assert.Equal(20, book.MasterLevel);
    }

    [Fact]
    public void GetByItemId_UsesCaseInsensitivePropertiesCommentsAndTrailingCommas()
    {
        const string json = """
        {
          // generated from Item.wz skill stats
          "ITEMS": [
            {
              "ITEMID": 2280001,
              "SKILLIDS": [2001002,],
              "SUCCESSRATE": 100,
              "REQSKILLLEVEL": 1,
              "MASTERLEVEL": 15,
            },
          ],
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var catalog = new JsonSkillBookCatalog(stream);

        var book = catalog.GetByItemId(2280001);

        Assert.NotNull(book);
        Assert.Equal(2001002, Assert.Single(book!.SkillIds));
        Assert.Equal(100, book.SuccessRate);
        Assert.Equal(1, book.ReqSkillLevel);
        Assert.Equal(15, book.MasterLevel);
    }
}
