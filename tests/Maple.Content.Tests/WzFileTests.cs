using Maple.Content.Wz;

namespace Maple.Content.Tests;

public sealed class WzFileTests
{
    [Fact]
    public void String_Wz_Opens_And_Has_Top_Level_Directories()
    {
        using var wz = WzFile.Open(@"D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client\String.wz");
        Assert.NotNull(wz.Root);
        Assert.NotEmpty(wz.Root.Children);
    }

    [Fact]
    public void List_Wz_Opens_Successfully()
    {
        using var wz = WzFile.Open(@"D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client\List.wz");
        Assert.NotNull(wz.Root);
    }
}
