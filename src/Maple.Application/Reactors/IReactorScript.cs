namespace Maple.Application.Reactors;

/// <summary>已載入的 reactor 腳本；對照 Java ReactorScriptManager 呼叫 act()。</summary>
public interface IReactorScript
{
    void Act();
}

/// <summary>依 reactorId 建立 reactor 腳本實例。找不到對應 .js 回 <c>null</c>。</summary>
public interface IReactorScriptFactory
{
    IReactorScript? TryCreate(int reactorId, IReactorScriptContext rm);
}
