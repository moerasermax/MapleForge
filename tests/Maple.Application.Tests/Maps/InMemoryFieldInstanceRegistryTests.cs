using Maple.Application.Maps;

namespace Maple.Application.Tests.Maps;

/// <summary>P063（M4-2 世界 tick）：<see cref="InMemoryFieldInstanceRegistry.All"/> 供背景排程器
/// 巡邏所有已建立過的 field。</summary>
public sealed class InMemoryFieldInstanceRegistryTests
{
    [Fact]
    public void All_EmptyRegistry_ReturnsEmpty()
    {
        var registry = new InMemoryFieldInstanceRegistry();

        Assert.Empty(registry.All);
    }

    [Fact]
    public void All_AfterGetOrCreate_ReturnsEveryDistinctField()
    {
        var registry = new InMemoryFieldInstanceRegistry();
        var first = registry.GetOrCreate(100000000, out _);
        var second = registry.GetOrCreate(100000001, out _);
        var againFirst = registry.GetOrCreate(100000000, out var createdAgain);

        Assert.False(createdAgain);
        Assert.Same(first, againFirst);
        Assert.Equal(2, registry.All.Count);
        Assert.Contains(first, registry.All);
        Assert.Contains(second, registry.All);
    }
}
