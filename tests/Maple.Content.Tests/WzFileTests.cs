using Maple.Content.Wz;
using Maple.Core.Data;

namespace Maple.Content.Tests;

public sealed class WzFileTests
{
    private const string StringWzPath = @"E:\WorkSpace_離線資料\02_遊戲素材_game-assets\MapleStory\v113_Client\String.wz";
    private const string ListWzPath = @"E:\WorkSpace_離線資料\02_遊戲素材_game-assets\MapleStory\v113_Client\List.wz";

    [Fact]
    public void String_Wz_Opens_And_Has_Top_Level_Directories()
    {
        using var wz = WzFile.Open(StringWzPath);
        Assert.NotNull(wz.Root);
        Assert.NotEmpty(wz.Root.Children);
    }

    [Fact]
    public void List_Wz_Opens_Successfully()
    {
        using var wz = WzFile.Open(ListWzPath);
        Assert.NotNull(wz.Root);
    }

    [Fact]
    public void String_Wz_Root_Contains_Common_String_Images()
    {
        using var wz = WzFile.Open(StringWzPath);
        Assert.Contains("Eqp.img", wz.Root.Children.Keys);
        Assert.Contains("Etc.img", wz.Root.Children.Keys);
        Assert.Contains("Cash.img", wz.Root.Children.Keys);
    }

    [Fact]
    public void String_Wz_Eqp_Image_Contains_Known_Item_Name()
    {
        using var wz = WzFile.Open(StringWzPath);
        var eqpImage = Assert.IsType<WzImage>(wz.Root.Children["Eqp.img"]);
        var eqpRoot = Assert.IsType<Dictionary<string, WzProperty>>(eqpImage.Properties["Eqp"].Value);
        var capRoot = Assert.IsType<Dictionary<string, WzProperty>>(eqpRoot["Cap"].Value);
        var item = Assert.IsType<Dictionary<string, WzProperty>>(capRoot["1000000"].Value);
        Assert.Equal("藍色毛帽", Assert.IsType<string>(item["name"].Value));
    }

    [Fact]
    public void String_Wz_Detects_Version_113()
    {
        using var wz = WzFile.Open(StringWzPath);
        Assert.Equal(113, wz.Version);
    }
}

public sealed class WzDataProviderTests : IDisposable
{
    private const string WzDir = @"E:\WorkSpace_離線資料\02_遊戲素材_game-assets\MapleStory\v113_Client";
    private readonly WzDataProvider _provider = new(WzDir);

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void GetRoot_Returns_String_Root_With_Children()
    {
        var root = _provider.GetRoot("String");
        Assert.NotNull(root);
        Assert.NotEmpty(root.Children);
    }

    [Fact]
    public void GetAt_Traverses_Path_To_Known_Value()
    {
        // Eqp.img / Eqp / Cap / 1000000 / name = "藍色毛帽"
        var node = _provider.GetAt("String", "Eqp.img/Eqp/Cap/1000000/name");
        Assert.NotNull(node);
        Assert.Equal("藍色毛帽", node.Value);
    }

    [Fact]
    public void GetAt_Traverses_QuestInfo_With_Long_Unicode_String()
    {
        var node = _provider.GetAt("Quest", "QuestInfo.img/10001/1");

        Assert.NotNull(node);
        var text = Assert.IsType<string>(node.Value);
        Assert.Equal(177, text.Length);
        Assert.Contains("特殊幹員O", text);
    }

    [Fact]
    public void GetAt_Returns_Null_For_Missing_Path()
    {
        var node = _provider.GetAt("String", "Eqp.img/DoesNotExist/Nope");
        Assert.Null(node);
    }

    [Fact]
    public void GetRoot_Same_File_Returns_Cached_Instance()
    {
        var root1 = _provider.GetRoot("String");
        var root2 = _provider.GetRoot("String");
        Assert.Same(root1, root2);
    }
}
