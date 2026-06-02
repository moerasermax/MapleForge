using Maple.Core.Characters;

namespace Maple.Core.World;

/// <summary>
/// 入世玩家（執行期領域實體）：以**組合**持有持久 <see cref="Characters.Character"/> + 執行期位置/狀態，
/// 而非擴充 Character（持久文件不被每 tick 變動污染）。Core 富領域、**零傳輸**（不持 socket/送包委派）。
/// 之後擴充：即時 HP/MP(Vitals)、buff、行為(TakeDamage/Heal/ApplyBuff…)。
/// </summary>
public sealed class Player : IFieldObject
{
    /// <summary>持久角色資料（唯一在存檔/換圖/登出時由 Player 回寫）。</summary>
    public Character Character { get; }

    /// <summary>執行期位置（唯一位置真相；session 中途不從 Character 讀）。</summary>
    public Position Position { get; private set; }

    /// <summary>玩家以角色 id 充當地圖物件 id（怪/NPC/掉落由 FieldInstance 另配發）。</summary>
    public int ObjectId => Character.Id;

    public FieldObjectType Type => FieldObjectType.Player;

    public Player(Character character, Position spawn)
    {
        Character = character;
        Position = spawn;
    }

    /// <summary>套用移動最終位置（由 Application 用例在解析客戶端移動後呼叫）。</summary>
    public void MoveTo(Position position) => Position = position;

    /// <summary>
    /// 增減楓幣（meso）。富領域不變式：餘額不為負（扣超過持有時夾到 0）、不溢位。
    /// NPC 腳本 cm.gainMeso 等行為經此入口，而非直接寫 Character.Meso。
    /// </summary>
    public void GainMeso(int delta)
    {
        var next = (long)Character.Meso + delta;
        if (next < 0) next = 0;
        if (next > int.MaxValue) next = int.MaxValue;
        Character.Meso = (int)next;
    }
}
