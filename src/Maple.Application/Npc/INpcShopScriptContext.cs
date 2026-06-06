namespace Maple.Application.Npcs;

/// <summary>NPC 腳本可選商店能力（cm.openShop）。與基礎對話 surface 分開，降低既有腳本 API 變更面。</summary>
public interface INpcShopScriptContext
{
    void OpenShop(int shopOrNpcId);
}
