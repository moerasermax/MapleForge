using System.Text;
using Maple.Content.Skills;

namespace Maple.Content.Tests.Skills;

public sealed class JsonSkillBookCatalogTests
{
    [Fact]
    public void DefaultCatalog_LoadsExtractedV113SkillBooks()
    {
        var catalog = new JsonSkillBookCatalog(ResolveDefaultCatalogPath());

        Assert.True(catalog.Count > 0);

        var masteryBook = catalog.GetByItemId(2280000);
        Assert.NotNull(masteryBook);
        Assert.Equal(2121003, Assert.Single(masteryBook!.SkillIds));
        Assert.Equal(100, masteryBook.SuccessRate);
        Assert.Equal(0, masteryBook.ReqSkillLevel);
        Assert.Equal(10, masteryBook.MasterLevel);

        var skillBook = catalog.GetByItemId(2290096);
        Assert.NotNull(skillBook);
        Assert.Equal(
            new[]
            {
                1121000, 1221000, 1321000, 2121000, 2221000,
                2321000, 3121000, 3221000, 4121000, 4221000,
                5121000, 5221000, 21121000
            },
            skillBook!.SkillIds);
        Assert.Equal(70, skillBook.SuccessRate);
        Assert.Equal(5, skillBook.ReqSkillLevel);
        Assert.Equal(20, skillBook.MasterLevel);
    }

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

    private static string ResolveDefaultCatalogPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Maple.Content", "Skills", "minimal-skill-books.v113.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Skills", "minimal-skill-books.v113.json");
    }
}
