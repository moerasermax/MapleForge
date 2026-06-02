namespace Maple.Scripting;

/// <summary>NPC 腳本引擎設定（由 Host 從實例設定投影）。</summary>
public sealed class NpcScriptOptions
{
    /// <summary>腳本根目錄（其下需有 <c>npc/{npcId}.js</c>）。</summary>
    public string ScriptsDirectory { get; set; } = "scripts";
}
