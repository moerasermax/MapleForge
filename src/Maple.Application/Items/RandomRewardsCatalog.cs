namespace Maple.Application.Items;

/// <summary>
/// 對照 Java <c>server.RandomRewards</c> + <c>constants.GameConstants.goldrewards/silverrewards</c>：
/// 金/銀寶箱權重獎勵表。Java 原始資料是硬編 Java 常數陣列（非 WZ、非 SQL），故此處直接忠實移植數值。
/// DI singleton（每實例一份，非 static 可變狀態），<see cref="Random"/> 為 instance 欄位。
/// </summary>
public sealed class RandomRewardsCatalog
{
    // itemId, weight — 對照 Java GameConstants.goldrewards（逐項含中文品名註解，保留原順序）。
    private static readonly (int ItemId, int Weight)[] GoldRewards =
    [
        // 裝備
        (1302059, 3), // 龍泉劍
        (1402037, 1), // 龍背刃
        (1092049, 1), // 致命劍盾
        (1102041, 1), // 粉紅冒險家披風
        (1432018, 3), // 藍色滑雪板
        (1022047, 3), // 貓頭鷹
        (1432011, 3), // 佛羅利刃
        (1442020, 3), // 飛翔斧
        (1382035, 3), // 怒濤之杖
        (1372010, 3), // 鬼頭杖
        (1332027, 3), // 霸傑之刃
        (1302056, 3), // 紫凌劍
        (1402005, 3), // 斬魔刀
        (1472053, 3), // 克利思拳套
        (1462018, 3), // 可撒之弩
        (1452017, 3), // 梅杜斯
        (1422013, 3), // 鐳奧釘錘
        (1322029, 3), // 毀滅之鎚
        (1412010, 3), // 格洛斧
        (1472051, 1), // 綠色龍牙拳刃
        (1482013, 1), // 龍王之爪
        (1492013, 1), // 聖龍金槍
        (1382050, 1), // 玄武之杖
        (1382045, 1), // 火雲長杖
        (1382047, 1), // 冰魄長杖
        (1382048, 1), // 狂雷長杖
        (1382046, 1), // 毒龍長杖
        (1442018, 3), // 冷凍金槍魚
        (1332032, 4), // 聖誕樹
        (1482025, 3), // 粉紅色花紋游泳圈
        // 技能書
        (2290096, 1), // 楓葉祝福 20
        (2290049, 1), // 天怒 30
        (2290041, 1), // 火流星 30
        (2290047, 1), // 暴風雪 30
        (2290095, 1), // 煙霧彈 30
        (2290017, 1), // 鬥氣爆發 30
        (2290075, 1), // 必殺狙擊 30
        (2290085, 1), // 三飛閃 30
        (2290116, 1), // 海鷗特戰隊 30
        // 卷軸
        (2049100, 1), // 混沌卷軸60%
        (2040914, 1), // 盾牌攻擊卷軸60%
        (2040900, 4), // 盾牌防禦卷軸100%
        (2030008, 5), // 咖啡牛奶
        // 藥水
        (2000005, 10), // 超級藥水
        (2000004, 10), // 特殊藥水
        // 椅子
        (3010051, 1), // 公砂兔椅
        (3010020, 1), // 澎澎檜木桶
        // 其他
        (4001011, 4), // 猴子橡皮擦
        (4001010, 4), // 蘑菇王橡皮擦
        (4001009, 4), // 木妖橡皮擦
        (4280000, 4), // 金寶箱
    ];

    // itemId, weight — 對照 Java GameConstants.silverrewards。
    private static readonly (int ItemId, int Weight)[] SilverRewards =
    [
        // 裝備
        (1002452, 3), // 黑星白頭巾
        (1002455, 3), // 黑星紅頭巾
        (1102082, 1), // 破舊的黑色披風
        (1302049, 1), // 光線鞭子
        (1102041, 1), // 粉紅冒險家披風
        (1452019, 2), // 白色雷鳥弓
        (1022060, 2), // 狐猴眼部裝飾
        (1432011, 3), // 佛羅利刃
        (1442020, 3), // 飛翔斧
        (1382035, 3), // 怒濤之杖
        (1372010, 3), // 鬼頭杖
        (1332027, 3), // 霸傑之刃
        (1302056, 3), // 紫凌劍
        (1402005, 3), // 斬魔刀
        (1472053, 3), // 克利思拳套
        (1462018, 3), // 可撒之弩
        (1452017, 3), // 梅杜斯
        (1422013, 3), // 鐳奧釘錘
        (1322029, 3), // 毀滅之鎚
        (1412010, 3), // 格洛斧
        (1002587, 3), // 黑色烤栗販帽子
        (1402044, 1), // 南瓜燈籠
        (1442046, 1), // 超級滑雪板
        (1422031, 1), // 藍色海豹抱枕
        (1332054, 3), // 閃電飛刀
        (1012056, 3), // 狗鼻
        (1022047, 3), // 貓頭鷹
        (1442012, 3), // 天空雪板
        (1442018, 3), // 冷凍金槍魚
        (1432010, 3), // 奧丁手戟
        // 技能書
        (2290084, 1), // 三飛閃 20
        (2290048, 1), // 天怒 20
        (2290040, 1), // 火流星 20
        (2290046, 1), // 暴風雪 20
        (2290074, 1), // 必殺狙擊 20
        (2290064, 1), // 念力集中 20
        (2290094, 1), // 煙霧彈 20
        (2290022, 1), // 黑暗力量 20
        (2290056, 1), // 弓術精通 30
        (2290066, 1), // 弩術精通 30
        (2290020, 1), // 鬼神之擊 20
        // 藥水
        (2000005, 10), // 超級藥水
        (2000004, 10), // 特殊藥水
        // 椅子
        (3010041, 1), // 骷髏寶座
        (3012002, 1), // 檜木泡澡桶
        // 其他
        (4001116, 3), // 六角水晶項鍊
        (4001012, 3), // 大幽靈橡皮擦
        (4280001, 4), // 銀寶箱
    ];

    private readonly int[] _compiledGold;
    private readonly int[] _compiledSilver;
    private readonly Random _rng;

    public RandomRewardsCatalog(Random? random = null)
    {
        _rng = random ?? Random.Shared;
        _compiledGold = Compile(GoldRewards);
        _compiledSilver = Compile(SilverRewards);
    }

    /// <summary>
    /// 對照 Java <c>GameConstants.gachaponRareItem</c>：抽到這些道具時要全服廣播恭喜訊息。
    /// 回傳稀有度等級（0＝不稀有，不廣播；1~3＝稀有，數值本身在 Java 未被用來決定廣播內容，
    /// 只當作「是否 &gt; 0」的旗標，故這裡忠實保留等級數值但呼叫端目前只判斷是否為 0）。
    /// </summary>
    public static byte GetGachaponRareness(int itemId) => itemId switch
    {
        2022217 or 2022221 or 2022222 or 2022223 => 1,
        2370000 or 2370001 or 2370002 or 2370003 or 2370004 or 2370005 or 2370006 or 2370007
            or 3010054 or 2022483 or 2210029 => 2,
        2049100 or 1372039 or 1372040 or 1372041 or 1372042 or 1092049 or 1382037
            or 1102084 or 1102041 or 1102086 or 1102042 or 1082149 or 1082179 or 1402044
            or 3010065 or 3010064 or 3010068 or 3012001 or 3012002 or 3010020 or 3010041 => 3,
        _ => 0,
    };

    /// <summary>對照 Java <c>RandomRewards.getGoldBoxReward</c>。</summary>
    public int GetGoldBoxReward() => _compiledGold[_rng.Next(_compiledGold.Length)];

    /// <summary>對照 Java <c>RandomRewards.getSilverBoxReward</c>。</summary>
    public int GetSilverBoxReward() => _compiledSilver[_rng.Next(_compiledSilver.Length)];

    /// <summary>
    /// 對照 Java <c>RandomRewards.processRewards</c>：把 (itemId, weight) 攤平成每個 itemId 重複 weight 次的清單，
    /// 再均勻抽一筆＝加權隨機。Java 額外對攤平清單做 <c>Collections.shuffle</c>，但抽取本身已是均勻隨機索引，
    /// shuffle 對結果分佈無影響，故省略（純簡化，非行為變更）。
    /// </summary>
    private static int[] Compile((int ItemId, int Weight)[] table)
    {
        var list = new List<int>();
        foreach (var (itemId, weight) in table)
        {
            for (var i = 0; i < weight; i++)
            {
                list.Add(itemId);
            }
        }

        return list.ToArray();
    }
}
