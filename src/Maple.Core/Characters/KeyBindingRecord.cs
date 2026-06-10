namespace Maple.Core.Characters;

/// <summary>角色快捷鍵綁定文件模型。Key/Type/Action 對應客戶端鍵位概念，但不含版本 opcode 或封包排列。</summary>
public sealed class KeyBindingRecord
{
    public int Key { get; set; }

    public byte Type { get; set; }

    public int Action { get; set; }
}
