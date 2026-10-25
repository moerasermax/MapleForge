using Maple.Core.Inventory;
using Maple.Core.Items;
using Maple.Core.Shops;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Items;

public sealed record UseItemResult(
    bool Success,
    IReadOnlyList<PlayerStatUpdate> StatUpdates,
    ShopInventoryMutation? InventoryMutation,
    // P127：此次解除的異常（呼叫端送 cancelDebuff / cancelForeignDebuff）。
    IReadOnlyList<PlayerDisease>? CuredDiseases = null,
    // P164：持續型道具 buff（呼叫端送 giveBuff(-itemId)、他人 showBuffeffect／giveForeignBuff）。
    ItemBuffApplication? Buff = null)
{
    public static UseItemResult Failed() => new(false, Array.Empty<PlayerStatUpdate>(), null);
}

/// <summary>P164：道具 buff 套用結果；<paramref name="ShowForeignEffect"/> = Java applyBuffEffect 開頭「未變身」時的 showBuffeffect。</summary>
public sealed record ItemBuffApplication(MapleStatEffect Effect, PlayerBuffChange Buff, bool ShowForeignEffect);

public sealed class UseItemService
{
    private readonly IItemEffectCatalog _catalog;
    private readonly ISkillCatalog? _skills;

    public UseItemService(IItemEffectCatalog catalog, ISkillCatalog? skills = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _skills = skills;
    }

    public UseItemResult Use(Player player, short slot, int itemId, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!player.IsAlive)
        {
            return UseItemResult.Failed();
        }

        var item = player.Inventory.By(InventoryType.Use).Get(slot);
        if (item is null || item.ItemId != itemId || item.Quantity < 1)
        {
            return UseItemResult.Failed();
        }

        var effect = _catalog.GetEffect(itemId);
        if (effect is null)
        {
            return UseItemResult.Failed();
        }

        if (!player.TryTakeItemFromSlot(InventoryType.Use, slot, itemId, 1, out var mutation) || mutation is null)
        {
            return UseItemResult.Failed();
        }

        if (mutation.Removed)
        {
            player.Inventory.By(InventoryType.Use).TryTake(slot, out _);
        }

        var updates = ApplyEffect(player, effect, AlchemistOffset(player));
        var cured = effect.Cures?.Where(player.DispelDisease).ToArray();
        var buff = ApplyBuff(player, itemId, now ?? DateTimeOffset.UtcNow);
        player.FlushInventory();
        return new UseItemResult(true, updates, mutation, cured is { Length: > 0 } ? cured : null, buff);
    }

    /// <summary>
    /// P164：不消耗道具直接套用效果（Java <c>getItemEffect(id).applyTo(chr)</c>，如任務 buffItemID）：HP/MP、解除異常、持續 buff。
    /// </summary>
    public UseItemResult ApplyWithoutConsume(Player player, int itemId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(player);

        var effect = _catalog.GetEffect(itemId);
        if (effect is null)
        {
            return UseItemResult.Failed();
        }

        var updates = ApplyEffect(player, effect, AlchemistOffset(player));
        var cured = effect.Cures?.Where(player.DispelDisease).ToArray();
        return new UseItemResult(true, updates, null, cured is { Length: > 0 } ? cured : null, ApplyBuff(player, itemId, now));
    }

    /// <summary>
    /// P164：Java applyTo → <c>overTime</c> → applyBuffEffect：未變身時持續時間經 <c>alchemistModifyVal(withX=false)</c>（藥劑精通 y，
    /// 否則 100%），並對他人 showBuffeffect；變身道具改用變身 Selfstat（同技能變身）。
    /// </summary>
    private ItemBuffApplication? ApplyBuff(Player player, int itemId, DateTimeOffset now)
    {
        if (_catalog.GetBuffEffect(itemId) is not { } effect)
        {
            return null;
        }

        var notMorphed = player.GetBuffValue(MapleBuffStat.MORPH) is null;
        var duration = notMorphed ? effect.DurationMilliseconds * AlchemistOffset(player, withX: false) / 100 : effect.DurationMilliseconds;
        var statups = Maple.Application.Skills.SkillService.GetMorphStatups(effect, player.Character.Gender);
        return player.ApplyBuffStats(effect, now, statups, duration) is { } buff
            ? new ItemBuffApplication(effect, buff, notMorphed)
            : null;
    }

    /// <summary>
    /// P120：對照 Java <c>MapleStatEffect.alchemistModifyVal</c>（withX=true，HP 與 MP 都用 X）：
    /// 夜使者 411/412 的藥劑精通 4110000、暗夜行者 1411/1412 的 14110003 有學時 offset = X，否則 100。
    /// Java 另加 <c>RecoveryUP</c>（裝備潛能），MapleForge 尚無潛能，視為 0。
    /// </summary>
    private int AlchemistOffset(Player player, bool withX = true)
    {
        var skillId = player.Character.Job switch
        {
            411 or 412 => 4110000,
            1411 or 1412 => 14110003,
            _ => 0,
        };
        var level = skillId == 0 ? 0 : player.GetSkillLevel(skillId);
        return level > 0 && _skills?.GetSkill(skillId)?.GetEffect(level) is { } alchemist ? (withX ? alchemist.X : alchemist.Y) : 100;
    }

    private static IReadOnlyList<PlayerStatUpdate> ApplyEffect(Player player, ItemEffect effect, int alchemistOffset)
    {
        var stats = player.Character.Stats;
        var updates = new List<PlayerStatUpdate>(2);

        // P115：Java calcHPChange 以 getCurrentMaxHp（含裝備與 MAXHP buff）計算百分比與上限。
        // P120：固定值經 alchemistModifyVal（val * offset / 100），百分比不受影響。
        var hpRecovery = effect.Hp * alchemistOffset / 100 + (player.MaxHp * effect.HpRate / 100);
        if (hpRecovery > 0)
        {
            stats.Hp = (short)Math.Min(player.MaxHp, stats.Hp + hpRecovery);
            updates.Add(new PlayerStatUpdate(PlayerStatKind.Hp, stats.Hp));
        }

        var mpRecovery = effect.Mp * alchemistOffset / 100 + (player.MaxMp * effect.MpRate / 100);
        if (mpRecovery > 0)
        {
            stats.Mp = (short)Math.Min(player.MaxMp, stats.Mp + mpRecovery);
            updates.Add(new PlayerStatUpdate(PlayerStatKind.Mp, stats.Mp));
        }

        return updates;
    }
}
