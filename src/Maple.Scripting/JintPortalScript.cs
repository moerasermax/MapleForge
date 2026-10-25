using Jint;
using Jint.Native.Function;
using Maple.Application.Npcs;

namespace Maple.Scripting;

/// <summary>P170：portal .js 的 Jint 包裝：Start() → <c>enter(pi)</c>；portal 腳本沒有後續回應，Resume 不做事。</summary>
internal sealed class JintPortalScript : INpcScript
{
    private readonly Engine _engine;

    public JintPortalScript(Engine engine) => _engine = engine;

    public void Start()
    {
        if (_engine.GetValue("enter") is Function enter)
            _engine.Invoke(enter, _engine.GetValue("pi"));
    }

    public void Resume(int mode, int type, int selection)
    {
    }
}
