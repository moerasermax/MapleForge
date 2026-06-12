using Jint;
using Jint.Native.Function;
using Maple.Application.Reactors;

namespace Maple.Scripting;

internal sealed class JintReactorScript : IReactorScript
{
    private readonly Engine _engine;

    public JintReactorScript(Engine engine) => _engine = engine;

    public void Act()
    {
        if (_engine.GetValue("act") is Function callable)
        {
            _engine.Invoke(callable);
        }
    }
}
