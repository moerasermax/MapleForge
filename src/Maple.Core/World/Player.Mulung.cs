namespace Maple.Core.World;

/// <summary>
/// 武陵道場能量（P108，對照 Java <c>MapleCharacter.mulung_energy</c>／<c>mulungEnergyModify</c>）：
/// 執行期狀態，不持久化（Java 建構時歸零、未寫入 DB）。
/// </summary>
public sealed partial class Player
{
    public const short MaxMulungEnergy = 10_000;
    private const short MulungEnergyPerHit = 100;

    public short MulungEnergy { get; private set; }

    /// <summary>
    /// 對照 Java <c>mulungEnergyModify(inc)</c>：<c>true</c> 加 100（上限 10000），<c>false</c> 歸零。
    /// 回傳新值；呼叫端每次都要送能量封包（Java 不論是否變動都送）。
    /// </summary>
    public short ModifyMulungEnergy(bool increase)
    {
        MulungEnergy = increase
            ? (short)Math.Min(MaxMulungEnergy, MulungEnergy + MulungEnergyPerHit)
            : (short)0;
        return MulungEnergy;
    }
}
