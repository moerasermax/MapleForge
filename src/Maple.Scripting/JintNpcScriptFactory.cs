using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Jint;
using Maple.Application.Npcs;
using Microsoft.Extensions.Logging;

namespace Maple.Scripting;

/// <summary>
/// 用 Jint 跑既有 OdinMS NPC .js 腳本（不重寫腳本）。
/// **sandbox**：CLR 預設關閉（不呼叫 AllowClr）＋遞迴/敘述/逾時上限，擋惡意/失控腳本。
/// **相容層**：regex 剝除 load()/importPackage()，並注入 no-op shim 吃掉殘留 Java-ism。
/// cm 以「lowercase 委派物件」暴露給 JS（與 C# PascalCase 解耦，免命名策略不確定性）。
/// 預處理後的腳本原始碼依 npcId 快取。
/// </summary>
public sealed class JintNpcScriptFactory : INpcScriptFactory
{
    private readonly string _npcDir;
    private readonly ILogger<JintNpcScriptFactory> _log;
    private readonly ConcurrentDictionary<int, string?> _sourceCache = new();

    private static readonly Regex LoadCall = new(@"load\s*\([^)]*\)\s*;?", RegexOptions.Compiled);
    private static readonly Regex ImportPackageCall = new(@"importPackage\s*\([^)]*\)\s*;?", RegexOptions.Compiled);

    public JintNpcScriptFactory(NpcScriptOptions options, ILogger<JintNpcScriptFactory> log)
    {
        _npcDir = Path.Combine(options.ScriptsDirectory, "npc");
        _log = log;
    }

    public INpcScript? TryCreate(int npcId, INpcScriptContext cm)
    {
        var source = _sourceCache.GetOrAdd(npcId, LoadSource);
        if (source is null) return null;

        try
        {
            var engine = BuildEngine(cm);
            engine.Execute(source);
            return new JintNpcScript(engine);
        }
        catch (Exception ex)
        {
            _log.LogWarning("[NpcScript] npcId={Id} 腳本載入/執行失敗：{Msg}", npcId, ex.Message);
            return null;
        }
    }

    private string? LoadSource(int npcId)
    {
        var path = Path.Combine(_npcDir, $"{npcId}.js");
        if (!File.Exists(path)) return null;
        return Preprocess(File.ReadAllText(path));
    }

    /// <summary>剝除 Jint 不認的 Rhino/Nashorn Java 互通呼叫（shim 另外注入吃殘留）。</summary>
    private static string Preprocess(string raw)
    {
        raw = LoadCall.Replace(raw, string.Empty);
        raw = ImportPackageCall.Replace(raw, string.Empty);
        return raw;
    }

    private static Engine BuildEngine(INpcScriptContext cm)
    {
        var engine = new Engine(o => o
            .LimitRecursion(64)
            .MaxStatements(200_000)
            .TimeoutInterval(TimeSpan.FromSeconds(2)));

        Action<int> openShop = cm is INpcShopScriptContext shopContext
            ? shopContext.OpenShop
            : _ => { };

        // 殘留 Java-ism 的 no-op shim（剝不乾淨時不炸）
        engine.SetValue("load", new Action<object?>(_ => { }));
        engine.SetValue("importPackage", new Action<object?>(_ => { }));
        engine.SetValue("Packages", new { });

        // cm facade：lowercase 名＝腳本 API。送對話類只記 pending、領域類即時委派。
        engine.SetValue("cm", new CmFacade(cm, openShop));

        return engine;
    }

    private sealed class CmFacade
    {
        private readonly INpcScriptContext _cm;
        private readonly Action<int> _openShop;

        public CmFacade(INpcScriptContext cm, Action<int> openShop)
        {
            _cm = cm;
            _openShop = openShop;
        }

        public void sendNext(string text) => _cm.SendNext(text);
        public void sendPrev(string text) => _cm.SendPrev(text);
        public void sendNextPrev(string text) => _cm.SendNextPrev(text);
        public void sendOk(string text) => _cm.SendOk(text);
        public void sendYesNo(string text) => _cm.SendYesNo(text);
        public void sendSimple(string text) => _cm.SendSimple(text);
        public void sendGetText(string text) => _cm.SendGetText(text);
        public void sendGetNumber(string text, int def, int min, int max) => _cm.SendGetNumber(text, def, min, max);
        public void dispose() => _cm.Dispose();
        public void warp(int mapId) => _cm.Warp(mapId);
        public void openShop(int shopOrNpcId) => _openShop(shopOrNpcId);
        public void gainMeso(int amount) => _cm.GainMeso(amount);
        public void gainItem(int itemId, int quantity) => _cm.GainItem(itemId, quantity);
        public bool haveItem(int itemId) => _cm.HaveItem(itemId);
        public void openStorage() => _cm.OpenStorage();
        public void sendStorage() => _cm.SendStorage();
        public void startQuest(int questId) => _cm.StartQuest(questId);
        public void forceStartQuest(int questId, int npcId = 0, string? customData = null) => _cm.ForceStartQuest(questId, npcId, customData);
        public void completeQuest(int questId) => _cm.CompleteQuest(questId);
        public void forceCompleteQuest(int questId, int npcId = 0) => _cm.ForceCompleteQuest(questId, npcId);
        public int getQuestStatus(int questId) => _cm.GetQuestStatus(questId);
        public string getQuestCustomData(int questId) => _cm.GetQuestCustomData(questId);
        public void setQuestCustomData(int questId, string? customData) => _cm.SetQuestCustomData(questId, customData);
        public string getInfoQuest(int questId) => _cm.GetInfoQuest(questId);
        public void updateInfoQuest(int questId, string? data) => _cm.UpdateInfoQuest(questId, data);
        public void clearInfoQuest(int questId) => _cm.ClearInfoQuest(questId);
        public int getJob() => _cm.GetJob();
        public int getMeso() => _cm.GetMeso();
        public int getMap() => _cm.GetMap();
        public int getBuddyCapacity() => _cm.GetBuddyCapacity();
        public void updateBuddyCapacity(int capacity) => _cm.UpdateBuddyCapacity(capacity);
        public int getPlayerStat(string type) => _cm.GetPlayerStat(type);
        public void increaseGuildCapacity() => _cm.IncreaseGuildCapacity();
        public void disbandGuild() => _cm.DisbandGuild();
        public void sendRepairWindow() => _cm.SendRepairWindow();
    }
}
