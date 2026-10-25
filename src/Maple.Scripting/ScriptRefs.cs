namespace Maple.Scripting;

/// <summary>
/// P178：跨腳本引擎傳遞的參考（NPC 腳本的 <c>cm.getPlayer()</c>／<c>cm.getParty()</c>／<c>cm.getMap()</c> 傳給事件腳本的
/// <c>em.startInstance(party, map)</c>、<c>eim.registerPlayer(chr)</c>）。事件端只需要識別碼，再自己向世界查。
/// </summary>
public interface IScriptCharacterRef
{
    int CharacterId { get; }
}

public interface IScriptMapRef
{
    int MapId { get; }
}

public interface IScriptPartyRef
{
    int PartyId { get; }

    IReadOnlyList<int> MemberIds { get; }
}

/// <summary>P190：遠征隊參考（NPC 的 <c>cm.getSquad(type)</c> 傳給事件的 <c>em.startInstance(squad, map)</c>）。</summary>
public interface IScriptSquadRef
{
    Maple.Core.Squads.MapleSquad Squad { get; }
}
