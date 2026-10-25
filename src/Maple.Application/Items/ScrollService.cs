using Maple.Core.Inventory;
using Maple.Core.Items;
using Maple.Core.World;

namespace Maple.Application.Items;

public enum ScrollResult
{
    Success,
    Fail,
    Curse,
}

public sealed record ScrollUseResult(
    ScrollResult Result,
    bool Applied,
    int ScrollId,
    short ScrollSlot,
    short EquipSlot,
    bool EquippedSlot,
    bool WhiteScrollUsed,
    Equip? UpdatedEquip,
    bool EquipDestroyed,
    IReadOnlyList<InventoryQuantityMutation> InventoryMutations);

public sealed class ScrollService
{
    public const int WhiteScrollItemId = 2340000;

    private readonly IScrollCatalog _scrolls;
    private readonly Random _random;
    private readonly IEquipTemplateProvider? _equipTemplates;

    /// <param name="random">P121：伺服器端亂數（Java Randomizer）。原本以客戶端送來的 tick 當種子，結果可被客戶端操控。</param>
    /// <param name="equipTemplates">P154：裝備模板（tuc 上限、現金裝備判斷）。</param>
    public ScrollService(IScrollCatalog scrolls, Random? random = null, IEquipTemplateProvider? equipTemplates = null)
    {
        ArgumentNullException.ThrowIfNull(scrolls);
        _scrolls = scrolls;
        _random = random ?? Random.Shared;
        _equipTemplates = equipTemplates;
    }

    /// <summary>Java <c>GameConstants.isCleanSlate</c>（20490xx 恢復卷軸）。</summary>
    public static bool IsCleanSlate(int scrollId) => scrollId / 100 == 20490;

    /// <summary>Java <c>isChaosScroll</c>（20491xx，排除 2049105~2049110）。</summary>
    public static bool IsChaosScroll(int scrollId) => scrollId is not (>= 2049105 and <= 2049110) && scrollId / 100 == 20491;

    /// <summary>Java <c>isSpecialScroll</c>（防滑鞋 2040727、保暖披風 2041058）。</summary>
    public static bool IsSpecialScroll(int scrollId) => scrollId is 2040727 or 2041058;

    public static bool IsAccessoryScroll(int scrollId) => scrollId / 100 == 20492;

    public static bool IsEquipEnhanceScroll(int scrollId) => scrollId / 100 == 20493;

    public static bool IsPotentialScroll(int scrollId) => scrollId / 100 == 20494;

    public static bool IsTablet(int scrollId) => scrollId / 1000 == 2047;

    /// <summary>Java <c>GameConstants.isAccessory</c>。</summary>
    public static bool IsAccessory(int itemId)
        => itemId is >= 1010000 and < 1040000 or >= 1122000 and < 1153000 or >= 1112000 and < 1113000;

    /// <summary>
    /// P154：對照 Java <c>InventoryHandler.UseUpgradeScroll</c> 的使用判斷（全部在消耗卷軸之前，失敗不消耗）。
    /// 裝備強化卷軸（20493xx）、潛能卷軸（20494xx）、平板（2047xxx）需要強化次數／潛能／耐久欄位，MapleForge 尚無，一律拒絕。
    /// </summary>
    private bool CanUse(ScrollEffect scroll, Equip equip)
    {
        var id = scroll.ScrollId;
        if (IsEquipEnhanceScroll(id) || IsPotentialScroll(id) || IsTablet(id))
        {
            return false;
        }

        if (!IsSpecialScroll(id) && !IsCleanSlate(id) && equip.UpgradeSlots < 1)
        {
            return false;
        }

        // Java canScroll：騎寵（19）與機甲（16）裝備不可上卷。
        if (equip.ItemId / 100000 is 19 or 16)
        {
            return false;
        }

        if ((IsCleanSlate(id) || IsChaosScroll(id)) && _equipTemplates?.IsCash(equip.ItemId) == true)
        {
            return false;
        }

        if (scroll.RequiredItems is { Count: > 0 } required && !required.Contains(equip.ItemId))
        {
            return false;
        }

        if (id == 2049115 && equip.ItemId != 1003068)
        {
            return false;
        }

        // Java ii.canScroll(scroll, item)：一般卷軸 (scroll / 100) % 100 == (item / 10000) % 100。
        if (!IsAccessoryScroll(id) && !IsChaosScroll(id) && !IsCleanSlate(id) && id / 100 % 100 != equip.ItemId / 10000 % 100)
        {
            return false;
        }

        return !IsAccessoryScroll(id) || IsAccessory(equip.ItemId);
    }

    public ScrollUseResult UseScroll(
        Player player,
        short scrollSlot,
        short equipSlot,
        bool whiteScroll)
    {
        ArgumentNullException.ThrowIfNull(player);

        var scrollItem = player.Inventory.By(InventoryType.Use).Get(scrollSlot);
        if (scrollItem is null || scrollItem.Quantity <= 0)
        {
            return NotApplied(scrollSlot, equipSlot);
        }

        var scroll = _scrolls.GetScroll(scrollItem.ItemId);
        if (scroll is null)
        {
            return NotApplied(scrollItem.ItemId, scrollSlot, equipSlot);
        }

        var target = ResolveTarget(player, equipSlot);
        if (target.Equip is null || !CanUse(scroll, target.Equip))
        {
            return NotApplied(scroll.ScrollId, scrollSlot, equipSlot);
        }

        if (!player.TryConsumeInventoryItem(
                InventoryType.Use,
                scrollSlot,
                scroll.ScrollId,
                1,
                out var scrollMutation) ||
            scrollMutation is null)
        {
            return NotApplied(scroll.ScrollId, scrollSlot, equipSlot);
        }

        var mutations = new List<InventoryQuantityMutation> { scrollMutation };
        var whiteScrollUsed = false;
        if (whiteScroll && player.TryConsumeUseItemById(WhiteScrollItemId, 1, out var whiteMutations))
        {
            whiteScrollUsed = true;
            mutations.AddRange(whiteMutations);
        }

        var equip = target.Equip;
        var oldLevel = equip.Level;
        var oldFlag = equip.Flag;
        var oldSlots = equip.UpgradeSlots;
        var consumesSlot = !IsCleanSlate(scroll.ScrollId) && !IsSpecialScroll(scroll.ScrollId);
        var destroyed = false;
        if (_random.Next(100) <= scroll.SuccessRate)
        {
            ApplySuccess(equip, scroll);
            if (consumesSlot)
            {
                equip.UpgradeSlots = (byte)Math.Max(0, equip.UpgradeSlots - 1);
                equip.Level = (byte)Math.Min(byte.MaxValue, equip.Level + 1);
            }
        }
        else
        {
            if (!whiteScrollUsed && consumesSlot)
            {
                equip.UpgradeSlots = (byte)Math.Max(0, equip.UpgradeSlots - 1);
            }

            destroyed = scroll.CurseRate > 0 && _random.Next(99) < scroll.CurseRate;
        }

        // Java UseUpgradeScroll 的結果判定：毀損 → CURSE；等級／旗標增加或恢復卷軸次數增加 → SUCCESS；其他 → FAIL。
        var result = destroyed ? ScrollResult.Curse
            : equip.Level > oldLevel || equip.Flag > oldFlag || (IsCleanSlate(scroll.ScrollId) && equip.UpgradeSlots > oldSlots)
                ? ScrollResult.Success
                : ScrollResult.Fail;
        if (destroyed)
        {
            RemoveTarget(player, equipSlot, target.Entry);
        }
        else
        {
            CommitTarget(player, equipSlot, target.Entry, equip);
        }

        player.FlushInventory();
        return new ScrollUseResult(
            result,
            Applied: true,
            scroll.ScrollId,
            scrollSlot,
            equipSlot,
            EquippedSlot: equipSlot < 0,
            WhiteScrollUsed: whiteScrollUsed,
            UpdatedEquip: destroyed ? null : equip,
            EquipDestroyed: destroyed,
            InventoryMutations: mutations);
    }

    private static ScrollUseResult NotApplied(short scrollSlot, short equipSlot)
        => NotApplied(0, scrollSlot, equipSlot);

    private static ScrollUseResult NotApplied(int scrollId, short scrollSlot, short equipSlot)
        => new(
            ScrollResult.Fail,
            Applied: false,
            scrollId,
            scrollSlot,
            equipSlot,
            EquippedSlot: equipSlot < 0,
            WhiteScrollUsed: false,
            UpdatedEquip: null,
            EquipDestroyed: false,
            InventoryMutations: Array.Empty<InventoryQuantityMutation>());

    /// <summary>
    /// 對照 Java <c>scrollEquipWithId</c> 成功分支：恢復卷軸 2049000~2049005（+1）／2049006~2049008（+2）在
    /// <c>等級 + 剩餘次數 &lt; tuc</c> 時加回次數；防滑鞋 2040727 旗標 |= 0x02、保暖披風 2041058 |= 0x04；
    /// 混沌卷軸對每個 &gt; 0 的屬性 <c>+= nextInt(z) * (nextBoolean ? 1 : -1)</c>（z = 2049116 ? 10 : 5，順序同 Java）；
    /// 其他一般卷軸加上 inc 屬性。次數／等級的扣加由呼叫端處理。
    /// </summary>
    private void ApplySuccess(Equip equip, ScrollEffect scroll)
    {
        switch (scroll.ScrollId)
        {
            case >= 2049000 and <= 2049005:
                if (equip.Level + equip.UpgradeSlots < MaxUpgradeSlots(equip.ItemId))
                {
                    equip.UpgradeSlots++;
                }

                return;
            case >= 2049006 and <= 2049008:
                if (equip.Level + equip.UpgradeSlots < MaxUpgradeSlots(equip.ItemId))
                {
                    equip.UpgradeSlots += 2;
                }

                return;
            case 2040727:
                equip.Flag |= 0x02;
                return;
            case 2041058:
                equip.Flag |= 0x04;
                return;
        }

        if (IsChaosScroll(scroll.ScrollId))
        {
            var z = scroll.ScrollId == 2049116 ? 10 : 5;
            short Chaos(short value) => value > 0 ? (short)(value + _random.Next(z) * (_random.Next(2) == 1 ? 1 : -1)) : value;
            equip.Str = Chaos(equip.Str);
            equip.Dex = Chaos(equip.Dex);
            equip.Int = Chaos(equip.Int);
            equip.Luk = Chaos(equip.Luk);
            equip.Watk = Chaos(equip.Watk);
            equip.Wdef = Chaos(equip.Wdef);
            equip.Matk = Chaos(equip.Matk);
            equip.Mdef = Chaos(equip.Mdef);
            equip.Acc = Chaos(equip.Acc);
            equip.Avoid = Chaos(equip.Avoid);
            equip.Speed = Chaos(equip.Speed);
            equip.Jump = Chaos(equip.Jump);
            equip.Hp = Chaos(equip.Hp);
            equip.Mp = Chaos(equip.Mp);
            return;
        }

        equip.Str += scroll.Str;
        equip.Dex += scroll.Dex;
        equip.Int += scroll.Int;
        equip.Luk += scroll.Luk;
        equip.Hp += scroll.Hp;
        equip.Mp += scroll.Mp;
        equip.Watk += scroll.Watk;
        equip.Matk += scroll.Matk;
        equip.Wdef += scroll.Wdef;
        equip.Mdef += scroll.Mdef;
        equip.Acc += scroll.Acc;
        equip.Avoid += scroll.Avoid;
        equip.Speed += scroll.Speed;
        equip.Jump += scroll.Jump;
    }

    /// <summary>Java <c>eqstats.get("tuc")</c>；無模板時 0（不加回次數）。</summary>
    private int MaxUpgradeSlots(int equipId) => _equipTemplates?.Create(equipId).UpgradeSlots ?? 0;

    private static ScrollTarget ResolveTarget(Player player, short equipSlot)
    {
        if (equipSlot < 0)
        {
            var entry = player.Character.Equips.FirstOrDefault(e => e.Position == equipSlot);
            return new ScrollTarget(entry?.ToEquip(), entry);
        }

        return new ScrollTarget(player.Inventory.By(InventoryType.Equip).Get(equipSlot) as Equip, null);
    }

    private static void CommitTarget(Player player, short equipSlot, Maple.Core.Characters.EquipEntry? entry, Equip equip)
    {
        if (equipSlot < 0)
        {
            entry?.CopyFrom(equip);
        }
    }

    private static void RemoveTarget(Player player, short equipSlot, Maple.Core.Characters.EquipEntry? entry)
    {
        if (equipSlot < 0)
        {
            if (entry is not null)
            {
                player.Character.Equips.Remove(entry);
            }

            return;
        }

        player.Inventory.By(InventoryType.Equip).TryTake(equipSlot, out _);
    }

    private readonly record struct ScrollTarget(Equip? Equip, Maple.Core.Characters.EquipEntry? Entry);
}
