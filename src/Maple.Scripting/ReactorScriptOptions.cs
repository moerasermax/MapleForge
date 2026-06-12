namespace Maple.Scripting;

/// <summary>Reactor 腳本引擎設定（根目錄下需有 <c>reactor/{reactorId}.js</c>）。</summary>
public sealed class ReactorScriptOptions
{
    public string ScriptsDirectory { get; set; } = "scripts";
}
