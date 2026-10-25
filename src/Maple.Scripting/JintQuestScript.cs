using Jint;
using Jint.Native.Function;
using Maple.Application.Npcs;

namespace Maple.Scripting;

/// <summary>P174：任務 .js 的 Jint 包裝：Start() → <c>start|end(1, 0, 0)</c>；Resume → <c>start|end(mode, type, selection)</c>。</summary>
internal sealed class JintQuestScript : INpcScript
{
    private readonly Engine _engine;
    private readonly string _function;

    public JintQuestScript(Engine engine, string function) => (_engine, _function) = (engine, function);

    public void Start() => Invoke(1, 0, 0);

    public void Resume(int mode, int type, int selection) => Invoke(mode, type, selection);

    private void Invoke(int mode, int type, int selection)
    {
        if (_engine.GetValue(_function) is Function callable)
            _engine.Invoke(callable, mode, type, selection);
    }
}
