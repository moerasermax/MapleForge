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

        // 殘留 Java-ism 的 no-op shim（剝不乾淨時不炸）
        engine.SetValue("load", new Action<object?>(_ => { }));
        engine.SetValue("importPackage", new Action<object?>(_ => { }));
        engine.SetValue("Packages", new { });

        // cm facade：lowercase 名＝腳本 API。送對話類只記 pending、領域類即時委派。
        engine.SetValue("cm", new
        {
            sendNext = (Action<string>)cm.SendNext,
            sendPrev = (Action<string>)cm.SendPrev,
            sendNextPrev = (Action<string>)cm.SendNextPrev,
            sendOk = (Action<string>)cm.SendOk,
            sendYesNo = (Action<string>)cm.SendYesNo,
            sendSimple = (Action<string>)cm.SendSimple,
            sendGetText = (Action<string>)cm.SendGetText,
            sendGetNumber = (Action<string, int, int, int>)cm.SendGetNumber,
            dispose = (Action)cm.Dispose,
            warp = (Action<int>)cm.Warp,
            gainMeso = (Action<int>)cm.GainMeso,
            getJob = (Func<int>)cm.GetJob,
            getMeso = (Func<int>)cm.GetMeso,
            getMap = (Func<int>)cm.GetMap,
        });

        return engine;
    }
}
