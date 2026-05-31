using Maple.Content.Wz;

namespace Maple.Content.Tests;

public sealed class WzFileTests
{
    private const string StringWzPath = @"D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client\String.wz";
    private const string ListWzPath = @"D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client\List.wz";

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
