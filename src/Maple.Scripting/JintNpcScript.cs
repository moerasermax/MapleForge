using Jint;
using Jint.Native;
using Jint.Native.Function;
using Maple.Application.Npcs;

namespace Maple.Scripting;

/// <summary>
/// 一個 OdinMS NPC .js 的 Jint 包裝：保留 Engine 實例與 JS 全域 scope，跨封包重入。
/// Start() → 腳本 start()；Resume() → 腳本 action(mode,type,selection)。
/// 找不到對應函式（例如只有 start()、無 action()）時靜默略過，不炸連線。
/// </summary>
internal sealed class JintNpcScript : INpcScript
{
    private readonly Engine _engine;

    public JintNpcScript(Engine engine) => _engine = engine;

    public void Start() => Invoke("start");

    public void Resume(int mode, int type, int selection) => Invoke("action", mode, type, selection);

    private void Invoke(string fn, params object[] args)
    {
        if (_engine.GetValue(fn) is Function callable)
            _engine.Invoke(callable, args);
    }
}
