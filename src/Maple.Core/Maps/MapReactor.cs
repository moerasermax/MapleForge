namespace Maple.Core.Maps;

/// <summary>
/// 地圖上的靜態 Reactor 定義（從 WZ map <c>reactor</c> 節點載入；不可變）。
/// 執行期 state / objectId / alive 狀態落在 Core/World <c>Reactor</c>。
/// </summary>
public sealed class MapReactor
{
    /// <summary>Reactor 模板 id（WZ Reactor.wz 的鍵）。</summary>
    public int ReactorId { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    /// <summary>朝向（WZ <c>f</c>，spawn 封包直接送）。</summary>
    public int F { get; init; }

    /// <summary>Java <c>reactorTime</c> 轉毫秒；0 表示不自動重生/延遲毀滅。</summary>
    public int ReactorTimeMs { get; init; }

    public string Name { get; init; } = string.Empty;
}
